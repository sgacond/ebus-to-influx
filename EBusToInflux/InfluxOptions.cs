namespace EBusToInflux;

/// <summary>InfluxDB 2.x target. Writing is disabled unless both Url and Token are configured.</summary>
internal sealed class InfluxOptions
{
    public const string Section = "Influx";

    public string Url { get; set; } = "";
    public string Org { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Token { get; set; } = "";
    public string Measurement { get; set; } = "heatpump";

    /// <summary>Unchanged values are written again after this interval, so graphs have no gaps.</summary>
    public TimeSpan RepeatInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Token);
}
