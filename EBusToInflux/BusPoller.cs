namespace EBusToInflux;

/// <summary>
/// Reads parameters actively: sends Weishaupt read requests (50 22) as bus master, one at a time, following the
/// eBus arbitration rules. It is fed every byte received from the coupler (which echoes our own bytes as well)
/// and returns the bytes to send. The answers are decoded by the normal parser path like any other telegram.
/// </summary>
/// <remarks>
/// Sequence: wait for an idle bus, send the own address right after a SYN and check its echo (otherwise another
/// master won the arbitration), send the rest, wait for the ACK and the answer, then ACK it and release the bus
/// with a SYN. Any deviation drops the request; it is retried in the next cycle.
/// </remarks>
internal sealed class BusPoller(byte masterAddress, byte targetAddress, IReadOnlyList<ushort> ids, TimeSpan interval)
{
    const byte Syn = 0xAA;
    const byte Esc = 0xA9;
    const byte Ack = 0x00;
    const byte Nack = 0xFF;
    const int MaxDataLength = 16;

    // own requests have to wait for this many SYNs in a row, so other masters always get the bus first
    const int IdleSynsBeforeSend = 2;
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    enum State { Idle, AwaitArbitrationEcho, AwaitEcho, AwaitAck, AwaitAnswer }

    readonly Queue<ushort> pending = new();
    readonly List<byte> answer = [];
    DateTimeOffset nextCycle = DateTimeOffset.MinValue;
    State state = State.Idle;
    int idleSyns;
    byte[] request = [];
    int echoIndex;
    bool answerEscape;
    DateTimeOffset requestStarted;

    public int Completed { get; private set; }
    public int Failed { get; private set; }
    public int LostArbitrations { get; private set; }

    public byte[] OnByte(byte b, DateTimeOffset now)
    {
        if (state != State.Idle && now - requestStarted > RequestTimeout)
            Fail();

        switch (state)
        {
            case State.Idle:
                return OnIdle(b, now);

            case State.AwaitArbitrationEcho:
                if (b == Syn)
                    return []; // SYNs that were already on the way before our address
                if (b != request[0])
                {
                    LostArbitrations++;
                    ResetToIdle();
                    return [];
                }
                state = State.AwaitEcho;
                echoIndex = 1;
                return request[1..];

            case State.AwaitEcho:
                if (b != request[echoIndex])
                    return FailAndRelease();
                if (++echoIndex == request.Length)
                    state = State.AwaitAck;
                return [];

            case State.AwaitAck:
                if (b != Ack)
                    return FailAndRelease();
                state = State.AwaitAnswer;
                answer.Clear();
                answerEscape = false;
                return [];

            case State.AwaitAnswer:
                return OnAnswerByte(b);
        }

        return [];
    }

    byte[] OnIdle(byte b, DateTimeOffset now)
    {
        if (b != Syn)
        {
            idleSyns = 0;
            return [];
        }

        idleSyns++;
        if (pending.Count == 0 && now >= nextCycle)
        {
            foreach (var id in ids)
                pending.Enqueue(id);
            nextCycle = now + interval;
        }

        if (pending.Count == 0 || idleSyns < IdleSynsBeforeSend)
            return [];

        request = BuildRequest(masterAddress, targetAddress, pending.Peek());
        requestStarted = now;
        state = State.AwaitArbitrationEcho;
        return [request[0]];
    }

    byte[] OnAnswerByte(byte b)
    {
        if (b == Syn)
        {
            Fail();
            return [];
        }

        if (answerEscape)
        {
            if (b > 0x01)
                return FailAndRelease();
            b = b == 0x00 ? Esc : Syn;
            answerEscape = false;
        }
        else if (b == Esc)
        {
            answerEscape = true;
            return [];
        }

        answer.Add(b);
        if (answer[0] > MaxDataLength)
            return FailAndRelease();
        if (answer.Count < answer[0] + 2)
            return [];

        var valid = TelegramParser.ComputeCrc(answer.ToArray().AsSpan(0, answer.Count - 1)) == answer[^1];
        if (valid)
        {
            pending.Dequeue();
            Completed++;
            ResetToIdle();
        }
        else
        {
            Fail();
        }
        return [valid ? Ack : Nack, Syn];
    }

    byte[] FailAndRelease()
    {
        Fail();
        return [Syn];
    }

    void Fail()
    {
        if (pending.Count > 0)
            pending.Dequeue(); // retried in the next cycle
        Failed++;
        ResetToIdle();
    }

    void ResetToIdle()
    {
        state = State.Idle;
        idleSyns = 0;
    }

    /// <summary>QQ ZZ 50 22 03 CK ID_lo ID_hi CRC, escaped for transmission.</summary>
    internal static byte[] BuildRequest(byte master, byte target, ushort id)
    {
        byte[] data = [(byte)id, (byte)(id >> 8)];
        byte[] telegram = [master, target, 0x50, 0x22, 0x03, WeishauptDecoder.Checksum(data), .. data];
        return TelegramParser.Escape([.. telegram, TelegramParser.ComputeCrc(telegram)]);
    }
}
