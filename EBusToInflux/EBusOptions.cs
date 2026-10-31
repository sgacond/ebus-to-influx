namespace EBusToInflux;

/// <summary>Connection to the eBus coupler, which streams the raw bus bytes over TCP.</summary>
internal sealed class EBusOptions
{
    public const string Section = "EBus";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 5000;
}
