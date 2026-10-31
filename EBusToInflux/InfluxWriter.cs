using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace EBusToInflux;

/// <summary>
/// Takes readings from the listener, keeps the ones the <see cref="RecordingPolicy"/> wants and writes them
/// to InfluxDB in batches. If InfluxDB is unreachable, lines are kept (up to a limit) and retried on the next flush.
/// </summary>
internal sealed partial class InfluxWriter(
    ChannelReader<Reading> readings,
    IHttpClientFactory httpClientFactory,
    IOptions<InfluxOptions> options,
    ILogger<InfluxWriter> logger) : BackgroundService
{
    public const string HttpClientName = "influx";
    const int MaxPendingLines = 50_000;

    readonly Lock pendingLock = new();
    readonly List<string> pending = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.IsEnabled)
        {
            LogDisabled(logger);
            await foreach (var _ in readings.ReadAllAsync(stoppingToken)) { } // keep the channel drained
            return;
        }

        LogEnabled(logger, settings.Url, settings.Bucket, settings.Measurement);
        var collecting = CollectAsync(new RecordingPolicy(settings.RepeatInterval), settings.Measurement, stoppingToken);
        try
        {
            using var timer = new PeriodicTimer(settings.FlushInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await FlushAsync(settings, stoppingToken);
        }
        finally
        {
            await collecting.ContinueWith(_ => { }, TaskScheduler.Default);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await FlushAsync(settings, timeout.Token);
        }
    }

    async Task CollectAsync(RecordingPolicy policy, string measurement, CancellationToken cancellationToken)
    {
        await foreach (var reading in readings.ReadAllAsync(cancellationToken))
        {
            if (!policy.ShouldWrite(reading))
                continue;

            lock (pendingLock)
            {
                if (pending.Count >= MaxPendingLines)
                    pending.RemoveAt(0);
                pending.Add(LineProtocol.Format(measurement, reading));
            }
        }
    }

    internal async Task FlushAsync(InfluxOptions settings, CancellationToken cancellationToken)
    {
        string[] batch;
        lock (pendingLock)
            batch = [.. pending];

        if (batch.Length == 0)
            return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{settings.Url.TrimEnd('/')}/api/v2/write?org={Uri.EscapeDataString(settings.Org)}&bucket={Uri.EscapeDataString(settings.Bucket)}&precision=s")
            {
                Content = new StringContent(string.Join('\n', batch), Encoding.UTF8, "text/plain"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", settings.Token);

            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                LogWriteFailed(logger, batch.Length, $"{(int)response.StatusCode} {body}");
                return;
            }

            lock (pendingLock)
                pending.RemoveRange(0, Math.Min(batch.Length, pending.Count));
            LogWritten(logger, batch.Length);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogWriteFailed(logger, batch.Length, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "InfluxDB writing disabled (Influx:Url or Influx:Token not configured)")]
    static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Writing to InfluxDB {Url}, bucket {Bucket}, measurement {Measurement}")]
    static partial void LogEnabled(ILogger logger, string url, string bucket, string measurement);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote {Count} points to InfluxDB")]
    static partial void LogWritten(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing {Count} points to InfluxDB failed, will retry: {Reason}")]
    static partial void LogWriteFailed(ILogger logger, int count, string reason);
}
