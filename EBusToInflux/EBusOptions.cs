namespace EBusToInflux;

/// <summary>Connection to the eBus coupler, which streams the raw bus bytes over TCP.</summary>
internal sealed class EBusOptions
{
    public const string Section = "EBus";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 5000;

    /// <summary>How often all measurements are read actively. Zero disables sending to the bus entirely.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.Zero;

    /// <summary>Own master address for read requests. 0xFF has the lowest arbitration priority.</summary>
    public byte MasterAddress { get; set; } = 0xFF;

    /// <summary>Slave address of the heat pump, which answers the read requests.</summary>
    public byte HeatPumpAddress { get; set; } = 0xF6;
}
