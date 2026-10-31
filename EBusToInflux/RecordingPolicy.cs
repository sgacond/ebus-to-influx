namespace EBusToInflux;

/// <summary>
/// Decides which readings are written: only available measurements, immediately when they change,
/// otherwise at most once per repeat interval.
/// </summary>
internal sealed class RecordingPolicy(TimeSpan repeatInterval)
{
    readonly Dictionary<ushort, (short Raw, DateTimeOffset Written)> lastWritten = [];

    public bool ShouldWrite(Reading reading)
    {
        var value = reading.Value;
        if (value.Parameter is not { Kind: ParameterKind.Measurement } || !value.IsAvailable)
            return false;

        if (lastWritten.TryGetValue(value.Id, out var last)
            && last.Raw == value.Raw
            && reading.Timestamp - last.Written < repeatInterval)
            return false;

        lastWritten[value.Id] = (value.Raw, reading.Timestamp);
        return true;
    }
}
