namespace EBusToInflux;

/// <summary>A decoded value as seen on the bus at a point in time.</summary>
internal sealed record Reading(DateTimeOffset Timestamp, WeishauptValue Value);
