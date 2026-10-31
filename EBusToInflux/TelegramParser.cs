namespace EBusToInflux;

/// <summary>
/// Byte-wise eBus telegram parser.
/// Master part:  QQ ZZ PB SB NN DB1..DBn CRC, then ACK from receiver (unless broadcast)
/// Slave answer: NN DB1..DBn CRC, then ACK from master (only if ZZ is a slave address)
/// 0xA9 and 0xAA in the data are transmitted escaped as A9 00 / A9 01.
/// </summary>
internal sealed class TelegramParser
{
    const byte Syn = 0xAA;
    const byte Esc = 0xA9;
    const byte Ack = 0x00;
    const byte BroadcastAddress = 0xFE;
    const int MaxDataLength = 16;

    static readonly byte[] CrcTable = BuildCrcTable();

    enum State
    {
        WaitSyn, Idle, Receiver, PrimaryCommand, SecondaryCommand, Length, Data, Crc, Ack,
        ResponseLength, ResponseData, ResponseCrc, ResponseAck, CompleteOnSyn
    }

    State state = State.Idle;
    bool escapePending;
    int bytesToRead;
    byte crc;

    // telegram under construction
    byte sender, receiver, primaryCommand, secondaryCommand, telegramCrc, responseCrc;
    readonly List<byte> payload = [];
    List<byte>? responsePayload;

    /// <summary>Number of telegrams discarded because of CRC, NACK or framing errors.</summary>
    public int ErrorCount { get; private set; }

    public Telegram? ContinueParse(byte b)
    {
        if (b == Syn)
            return OnSyn();

        if (state == State.WaitSyn)
            return null;

        if (escapePending)
        {
            escapePending = false;
            if (b > 0x01)
                return Abort();
            b = b == 0x00 ? Esc : Syn;
        }
        else if (b == Esc)
        {
            escapePending = true;
            return null;
        }

        return OnSymbol(b);
    }

    private Telegram? OnSyn()
    {
        // broadcasts and master-master telegrams are only complete once the SYN follows
        var result = state == State.CompleteOnSyn ? Build() : null;
        if (state is not (State.Idle or State.WaitSyn or State.CompleteOnSyn))
            ErrorCount++;
        Reset(State.Idle);
        return result;
    }

    private Telegram? OnSymbol(byte b)
    {
        switch (state)
        {
            case State.Idle:
                sender = b;
                crc = UpdateCrc(0, b);
                state = State.Receiver;
                break;

            case State.Receiver:
                receiver = b;
                crc = UpdateCrc(crc, b);
                state = State.PrimaryCommand;
                break;

            case State.PrimaryCommand:
                primaryCommand = b;
                crc = UpdateCrc(crc, b);
                state = State.SecondaryCommand;
                break;

            case State.SecondaryCommand:
                secondaryCommand = b;
                crc = UpdateCrc(crc, b);
                state = State.Length;
                break;

            case State.Length:
                if (b > MaxDataLength)
                    return Abort();
                crc = UpdateCrc(crc, b);
                bytesToRead = b;
                state = b == 0 ? State.Crc : State.Data;
                break;

            case State.Data:
                payload.Add(b);
                crc = UpdateCrc(crc, b);
                if (--bytesToRead == 0)
                    state = State.Crc;
                break;

            case State.Crc:
                if (b != crc)
                    return Abort();
                telegramCrc = b;
                state = receiver == BroadcastAddress ? State.CompleteOnSyn : State.Ack;
                break;

            case State.Ack:
                if (b != Ack)
                    return Abort();
                state = IsMasterAddress(receiver) ? State.CompleteOnSyn : State.ResponseLength;
                break;

            case State.ResponseLength:
                if (b > MaxDataLength)
                    return Abort();
                responsePayload = new List<byte>(b);
                crc = UpdateCrc(0, b);
                bytesToRead = b;
                state = b == 0 ? State.ResponseCrc : State.ResponseData;
                break;

            case State.ResponseData:
                responsePayload!.Add(b);
                crc = UpdateCrc(crc, b);
                if (--bytesToRead == 0)
                    state = State.ResponseCrc;
                break;

            case State.ResponseCrc:
                if (b != crc)
                    return Abort();
                responseCrc = b;
                state = State.ResponseAck;
                break;

            case State.ResponseAck:
                if (b != Ack)
                    return Abort();
                var result = Build();
                Reset(State.Idle);
                return result;

            case State.CompleteOnSyn:
                return Abort();
        }

        return null;
    }

    private Telegram Build() =>
        new(sender, receiver, primaryCommand, secondaryCommand, [.. payload], telegramCrc,
            responsePayload is null ? null : [.. responsePayload], responseCrc);

    private Telegram? Abort()
    {
        ErrorCount++;
        Reset(State.WaitSyn);
        return null;
    }

    private void Reset(State newState)
    {
        state = newState;
        escapePending = false;
        bytesToRead = 0;
        crc = 0;
        payload.Clear();
        responsePayload = null;
        responseCrc = 0;
    }

    /// <summary>Master addresses have both nibbles in {0x0, 0x1, 0x3, 0x7, 0xF}.</summary>
    internal static bool IsMasterAddress(byte address) =>
        IsMasterNibble(address >> 4) && IsMasterNibble(address & 0x0F);

    static bool IsMasterNibble(int n) => n is 0x0 or 0x1 or 0x3 or 0x7 or 0xF;

    /// <summary>The CRC is calculated over the escaped (transmitted) form of each byte.</summary>
    static byte UpdateCrc(byte crc, byte value) => value switch
    {
        Esc => CrcStep(CrcStep(crc, Esc), 0x00),
        Syn => CrcStep(CrcStep(crc, Esc), 0x01),
        _ => CrcStep(crc, value),
    };

    static byte CrcStep(byte crc, byte value) => (byte)(CrcTable[crc] ^ value);

    static byte[] BuildCrcTable()
    {
        var table = new byte[256];
        for (var i = 0; i < table.Length; i++)
        {
            var c = i;
            for (var bit = 0; bit < 8; bit++)
                c = (c & 0x80) != 0 ? (c << 1) ^ 0x9B : c << 1;
            table[i] = (byte)c;
        }
        return table;
    }
}
