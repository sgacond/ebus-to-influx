using EBusToInflux;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly:InternalsVisibleTo("EBusToInflux.Tests")]

var defaultsJson = Assembly.GetExecutingAssembly().GetManifestResourceStream("EBusToInflux.defaultConfig.json");
var config = new ConfigurationBuilder()
    .AddJsonStream(defaultsJson)
    .AddEnvironmentVariables()
    .Build();

var ebusSection = config.GetSection("ebus");
var host = ebusSection["host"];
var port = int.Parse(ebusSection["port"]);

var cts = new CancellationTokenSource();

using var client = new TcpClient(host, port);
using var stream = client.GetStream();

Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var buffer = new byte[256];
var parser = new TelegramParser();
var lastValues = new Dictionary<ushort, short>();
var seenUnknown = new HashSet<string>();

try
{
    byte lagB = 0x00;

    while (!cts.IsCancellationRequested)
    {
        var bytesToRead = await stream.ReadAsync(buffer, cts.Token);

        foreach(var b in buffer.Take(bytesToRead))
        {
            var result = parser.ContinueParse(b);
            if (result != null)
                PrintIfChanged(result);

            //if (lagB == 0xAA && b != 0xAA)
            //    Console.WriteLine();

            //if (lagB != 0xAA && b == 0xAA)
            //    Console.WriteLine();

            //Console.Write($"0x{b:X2} ");
            //lagB = b;
        }
    }
}
catch (OperationCanceledException)
{
    // all good... this is normal cancel procedure.
} 
finally
{
    client.Close();
    Console.WriteLine("Done, {0} invalid telegrams discarded", parser.ErrorCount);
}

// Probe output: one line per value whenever it is first seen or changes.
void PrintIfChanged(Telegram telegram)
{
    var value = WeishauptDecoder.Decode(telegram);
    if (value == null)
    {
        var signature = $"{telegram.Sender:X2}{telegram.Receiver:X2}{telegram.PrimaryCommand:X2}{telegram.SecondaryCommand:X2}";
        if (seenUnknown.Add(signature))
            Console.WriteLine("{0:HH:mm:ss}  unknown  {1}", DateTime.Now, telegram);
        return;
    }

    var hadPrevious = lastValues.TryGetValue(value.Id, out var previous);
    if (hadPrevious && previous == value.Raw)
        return;

    lastValues[value.Id] = value.Raw;
    Console.WriteLine("{0:HH:mm:ss}  50{1:X2}  0x{2:X4} ({2,5})  {3,-20} {4,6} {5,8}{6}",
        DateTime.Now, value.SecondaryCommand, value.Id, WeishauptDecoder.NameOf(value.Id),
        value.Raw, value.IsAvailable ? value.Scaled.ToString("0.0") : "n/a", hadPrevious ? $"   (was {previous / 10.0:0.0})" : "");
}
