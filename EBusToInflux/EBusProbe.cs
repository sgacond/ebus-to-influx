using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace EBusToInflux;

/// <summary>
/// Listens on the eBus coupler and prints every Weishaupt value when it is first seen or changes.
/// Reconnects automatically if the connection drops. Read-only: nothing is ever sent to the bus.
/// </summary>
internal sealed partial class EBusProbe(IOptions<EBusOptions> options, ILogger<EBusProbe> logger) : BackgroundService
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
        var buffer = new byte[256];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                foreach (var b in buffer.AsSpan(0, read))
                {
                    if (parser.ContinueParse(b) is { } telegram)
                        PrintIfChanged(telegram);
                }
            }
            throw new IOException("Connection closed by the coupler");
        }
        finally
        {
            LogDiscarded(logger, parser.ErrorCount);
        }
    }

    // Probe output goes to stdout as a plain table, diagnostics go through the logger.
    void PrintIfChanged(Telegram telegram)
    {
        if (WeishauptDecoder.Decode(telegram) is not { } value)
        {
            if (seenUnknown.Add((telegram.Sender, telegram.Receiver, telegram.PrimaryCommand, telegram.SecondaryCommand)))
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  unknown  {telegram}");
            return;
        }

        var hadPrevious = lastValues.TryGetValue(value.Id, out var previous);
        if (hadPrevious && previous == value.Raw)
            return;

        lastValues[value.Id] = value.Raw;
        var scaled = value.IsAvailable ? value.Scaled.ToString("0.0") : "n/a";
        var was = hadPrevious ? $"   (was {previous / 10.0:0.0})" : "";
        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss}  50{value.SecondaryCommand:X2}  0x{value.Id:X4} ({value.Id,5})  {WeishauptDecoder.NameOf(value.Id),-20} {value.Raw,6} {scaled,8}{was}");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to eBus coupler {Host}:{Port}")]
    static partial void LogConnected(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection to {Host}:{Port} lost ({Reason}), retrying in {Delay}")]
    static partial void LogConnectionLost(ILogger logger, string host, int port, string reason, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} invalid telegrams discarded")]
    static partial void LogDiscarded(ILogger logger, int count);
}
