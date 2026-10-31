using System.Globalization;

namespace EBusToInflux;

/// <summary>InfluxDB line protocol with second precision, one field per point.</summary>
internal static class LineProtocol
{
    public static string Format(string measurement, Reading reading) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Escape(measurement, ", ")} {Escape(reading.Value.Parameter!.Name, ", =")}={reading.Value.Value} {reading.Timestamp.ToUnixTimeSeconds()}");

    static string Escape(string text, string special)
    {
        foreach (var c in special)
            text = text.Replace(c.ToString(), $"\\{c}");
        return text;
    }
}
