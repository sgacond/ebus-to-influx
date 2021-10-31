namespace EBusToInflux
{
    public class Telegram
    {
        public byte Sender { get; set; }    
        public byte Receiver { get; set; }
        public byte PrimaryCommand { get; set; }
        public byte SecondaryCommand { get; set; }
        
        public List<byte> Payload { get; set; }
        public byte CRC { get; set; }
        
        public List<byte> ResponsePayload { get; set; }
        public byte ResponseCRC { get; set; }

        public override string ToString() =>
            ResponsePayload != null ?
                $"S: 0x{Sender:X2}, R: 0x{Receiver:X2}, CMD: 0x{PrimaryCommand:X2}/0x{SecondaryCommand:X2} PL: {string.Join(" ", Payload.Select(b => $"0x{b:X2}"))}, Reponse: {string.Join(" ", ResponsePayload.Select(b => $"0x{b:X2}"))}" :
                $"S: 0x{Sender:X2}, R: 0x{Receiver:X2}, CMD: 0x{PrimaryCommand:X2}/0x{SecondaryCommand:X2} PL: {string.Join(" ", Payload.Select(b => $"0x{b:X2}"))}";
    }
}
