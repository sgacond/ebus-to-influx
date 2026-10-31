using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace EBusToInflux;

/// <summary>
/// Listens on the eBus coupler, passes every decoded Weishaupt value on to the writer and prints it when it is
/// first seen or changes. Reconnects automatically if the connection drops. If a poll interval is configured,
/// the <see cref="BusPoller"/> additionally sends read requests for all measurements; nothing else is ever sent.
/// </summary>
internal sealed partial class EBusListener(
    IOptions<EBusOptions> options,
    ChannelWriter<Reading> readings,
    ILogger<EBusListener> logger) : BackgroundService
{
    static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);

    readonly Dictionary<ushort, short> lastValues = [];
    readonly HashSet<(byte, byte, byte, byte)> seenUnknown = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var (host, port) = (options.Value.Host, options.Value.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(host, port, stoppingToken);
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                LogConnectionLost(logger, host, port, ex.Message, ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    async Task ListenAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);
        LogConnected(logger, host, port);

        await using var stream = client.GetStream();
        var parser = new TelegramParser();
        var poller = CreatePoller();
        var buffer = new byte[256];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    if (poller?.OnByte(buffer[i], DateTimeOffset.UtcNow) is { Length: > 0 } toSend)
                        await stream.WriteAsync(toSend, cancellationToken);

                    if (parser.ContinueParse(buffer[i]) is { } telegram)
                        OnTelegram(telegram);
                }
            }
            throw new IOException("Connection closed by the coupler");
        }
        finally
        {
            LogDiscarded(logger, parser.ErrorCount);
            if (poller is not null)
                LogPollStatistics(logger, poller.Completed, poller.Failed, poller.LostArbitrations);
        }
    }

    BusPoller? CreatePoller()
    {
        var settings = options.Value;
        if (settings.PollInterval <= TimeSpan.Zero)
            return null;

        // parameters read from the heat pump (50 22); the ones from 0x06A0 on are written by the control unit
        ushort[] ids = [.. WeishauptParameters.Known
            .Where(p => p.Value.Kind == ParameterKind.Measurement && p.Key < 0x06A0)
            .Select(p => p.Key)
            .Order()];
        LogPolling(logger, ids.Length, settings.PollInterval, settings.MasterAddress);
        return new BusPoller(settings.MasterAddress, settings.HeatPumpAddress, ids, settings.PollInterval);
    }

    void OnTelegram(Telegram telegram)
    {
        if (WeishauptDecoder.Decode(telegram) is not { } value)
        {
            if (seenUnknown.Add((telegram.Sender, telegram.Receiver, telegram.PrimaryCommand, telegram.SecondaryCommand)))
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  unknown  {telegram}");
            return;
        }

        readings.TryWrite(new Reading(DateTimeOffset.UtcNow, value));

        // probe output goes to stdout as a plain table, diagnostics go through the logger
        var hadPrevious = lastValues.TryGetValue(value.Id, out var previous);
        if (hadPrevious && previous == value.Raw)
            return;

        lastValues[value.Id] = value.Raw;
        var parameter = value.Parameter;
        var name = parameter is null ? "" : parameter.Confirmed ? parameter.Name : parameter.Name + "?";
        var shown = value.IsAvailable ? $"{value.Value:0.#} {parameter?.Unit}" : "n/a";
        var was = hadPrevious ? $"   (was {(value with { Raw = previous }).Value:0.#})" : "";
        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss}  50{value.SecondaryCommand:X2}  0x{value.Id:X4} ({value.Id,5})  {name,-28} {value.Raw,6}  {shown,-10}{was}");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to eBus coupler {Host}:{Port}")]
    static partial void LogConnected(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection to {Host}:{Port} lost ({Reason}), retrying in {Delay}")]
    static partial void LogConnectionLost(ILogger logger, string host, int port, string reason, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Polling {Count} values every {Interval} as bus master 0x{Address:X2}")]
    static partial void LogPolling(ILogger logger, int count, TimeSpan interval, byte address);

    [LoggerMessage(Level = LogLevel.Information, Message = "Poll requests: {Completed} answered, {Failed} failed, {Lost} lost arbitration")]
    static partial void LogPollStatistics(ILogger logger, int completed, int failed, int lost);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} invalid telegrams discarded")]
    static partial void LogDiscarded(ILogger logger, int count);
}
