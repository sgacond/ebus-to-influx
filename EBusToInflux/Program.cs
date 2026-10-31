using System.Threading.Channels;
using EBusToInflux;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services
    .AddOptions<EBusOptions>()
    .BindConfiguration(EBusOptions.Section)
    .Validate(o => !string.IsNullOrWhiteSpace(o.Host), $"{EBusOptions.Section}:Host is required")
    .ValidateOnStart();

builder.Services
    .AddOptions<InfluxOptions>()
    .BindConfiguration(InfluxOptions.Section)
    .Validate(o => !o.IsEnabled || (!string.IsNullOrWhiteSpace(o.Org) && !string.IsNullOrWhiteSpace(o.Bucket)),
        $"{InfluxOptions.Section}:Org and Bucket are required")
    .ValidateOnStart();

// listener -> writer; drops the oldest readings if the writer cannot keep up
var readings = Channel.CreateBounded<Reading>(new BoundedChannelOptions(10_000)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = true,
    SingleWriter = true,
});
builder.Services.AddSingleton(readings.Reader);
builder.Services.AddSingleton(readings.Writer);

builder.Services.AddHttpClient(InfluxWriter.HttpClientName);
builder.Services.AddHostedService<EBusListener>();
builder.Services.AddHostedService<InfluxWriter>();

await builder.Build().RunAsync();
