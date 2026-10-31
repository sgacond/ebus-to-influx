using System.Buffers.Binary;

namespace EBusToInflux;

/// <summary>A single value from a Weishaupt 0x50 telegram.</summary>
internal sealed record WeishauptValue(byte SecondaryCommand, ushort Id, short Raw)
{
    public bool IsAvailable => Raw != short.MinValue; // 0x8000 = not available
    public double Scaled => Raw / 10.0;
}

/// <summary>
/// Decodes the Weishaupt-specific 0x50 telegrams (reverse engineered, no official spec):
/// 50 22: read parameter,  payload = CHK, ID lo, ID hi                         response = int16 LE
/// 50 23: write parameter, payload = CHK, ID lo, ID hi, int16 LE, 5D 01 00 00 (unknown, constant so far)
/// CHK is a CRC8 (poly 0x5C, eBus-style) over the rest of the payload. Temperatures are in 1/10 °C.
/// Same protocol as documented for the WTC boilers in
/// https://github.com/B-o-r-i-s-M/Weishaupt-WTC-OB-ebusd-Home-Assistant (docs/03-protokoll.md),
/// but the heat pump uses different parameter numbers.
/// </summary>
internal static class WeishauptDecoder
{
    const byte ChecksumPolynomial = 0x5C;

    // confirmed against the display of the control unit
    public static readonly IReadOnlyDictionary<ushort, string> KnownNames = new Dictionary<ushort, string>
    {
        [0x0641] = "OutsideTemperature",
        [0x0645] = "HeatPumpState",       // raw code: 0 = off, 3 = "Heizen"
        [0x0648] = "CompressorFrequency", // 1/10 Hz
        [0x0655] = "FlowTemperature",
        [0x0658] = "HotWaterTemperature",
        [0x06A5] = "OperatingMode",       // 3 = "Zeitprogramm 3", raw value (not /10)
        [0x06A8] = "RoomTemperature",
        [0x06B1] = "HotWaterTarget",
        [0x06BF] = "RoomTargetHeating?",  // 22.0, by value
        [0x06C1] = "ReducedRoomTargetHeating",
        [0x06C2] = "RoomTargetCooling",
        [0x06C4] = "ReducedRoomTargetCooling?", // 28.0, by value
    };

    public static WeishauptValue? Decode(Telegram t)
    {
        if (t.PrimaryCommand != 0x50 || !HasValidChecksum(t.Payload))
            return null;

        return (t.SecondaryCommand, t.Payload.Length, t.ResponsePayload?.Length) switch
        {
            (0x22, 3, 2) => new(t.SecondaryCommand, ReadId(t.Payload), BinaryPrimitives.ReadInt16LittleEndian(t.ResponsePayload!)),
            (0x23, 9, _) => new(t.SecondaryCommand, ReadId(t.Payload), BinaryPrimitives.ReadInt16LittleEndian(t.Payload.AsSpan(3))),
            _ => null,
        };
    }

    public static string NameOf(ushort id) => KnownNames.GetValueOrDefault(id, "");

    internal static bool HasValidChecksum(ReadOnlySpan<byte> payload) =>
        payload.Length > 1 && payload[0] == Checksum(payload[1..]);

    internal static byte Checksum(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data)
        {
            var c = crc;
            for (var bit = 0; bit < 8; bit++)
                c = (byte)((c & 0x80) != 0 ? (c << 1) ^ ChecksumPolynomial : c << 1);
            crc = (byte)(c ^ b);
        }
        return crc;
    }

    static ushort ReadId(byte[] payload) => BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(1));
}
