namespace EBusToInflux;

/// <summary>A complete eBus telegram. ResponsePayload is only set if the receiver is a slave.</summary>
internal sealed record Telegram(
    byte Sender,
    byte Receiver,
    byte PrimaryCommand,
    byte SecondaryCommand,
    byte[] Payload,
    byte Crc,
    byte[]? ResponsePayload = null,
    byte ResponseCrc = 0)
{
    public override string ToString() =>
        $"S: 0x{Sender:X2}, R: 0x{Receiver:X2}, CMD: 0x{PrimaryCommand:X2}/0x{SecondaryCommand:X2} PL: {Hex(Payload)}"
        + (ResponsePayload is null ? "" : $", Response: {Hex(ResponsePayload)}");

    static string Hex(byte[] bytes) => string.Join(' ', bytes.Select(b => $"0x{b:X2}"));
}
