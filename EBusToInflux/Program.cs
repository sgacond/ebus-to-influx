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

builder.Services.AddHostedService<EBusProbe>();

await builder.Build().RunAsync();
