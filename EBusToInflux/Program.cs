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
                Console.WriteLine("Telegram: {0}", result);

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
    Console.WriteLine("Done");
}
