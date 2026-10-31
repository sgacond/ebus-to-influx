namespace EBusToInflux.Tests;

public class BusPollerTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 31, 19, 0, 0, TimeSpan.Zero);
    static readonly byte[] OutsideRequest = Convert.FromHexString("FFF6502203224106C0"); // as sent in the manual test

    static BusPoller CreateSut(params ushort[] ids) => new(0xFF, 0xF6, ids, TimeSpan.FromMinutes(1));

    /// <summary>Feeds bytes as they appear on the bus and collects everything the poller wants to send.</summary>
    static List<byte> Feed(BusPoller sut, DateTimeOffset now, params ReadOnlySpan<byte> bytes)
    {
        var sent = new List<byte>();
        foreach (var b in bytes)
            sent.AddRange(sut.OnByte(b, now));
        return sent;
    }

    [Fact]
    public void RequestMatchesTheManualTest() =>
        Assert.Equal(OutsideRequest, BusPoller.BuildRequest(0xFF, 0xF6, 0x0641));

    [Fact]
    public void CompleteRequestAndAnswer()
    {
        var sut = CreateSut(0x0641);

        Assert.Empty(Feed(sut, T0, 0xAA));                                  // only one SYN, bus not idle long enough
        Assert.Equal([0xFF], Feed(sut, T0, 0xAA));                          // arbitration: own address
        Assert.Equal(OutsideRequest[1..], Feed(sut, T0, 0xFF));             // won, rest of the request
        Assert.Empty(Feed(sut, T0, OutsideRequest[1..]));                   // echo of the rest
        Assert.Empty(Feed(sut, T0, 0x00));                                  // ACK of the heat pump
        Assert.Equal([0x00, 0xAA], Feed(sut, T0, 0x02, 0x73, 0x00, 0x84));  // answer -> own ACK + SYN

        Assert.Equal(1, sut.Completed);
        Assert.Equal(0, sut.Failed);
    }

    [Fact]
    public void SynsBeforeTheEchoAreIgnored()
    {
        var sut = CreateSut(0x0641);
        Feed(sut, T0, 0xAA, 0xAA);

        Assert.Equal(OutsideRequest[1..], Feed(sut, T0, 0xAA, 0xAA, 0xFF));
    }

    [Fact]
    public void LostArbitrationSendsNothingAndRetries()
    {
        var sut = CreateSut(0x0641);
        Feed(sut, T0, 0xAA, 0xAA);

        // the control unit (0x30) started at the same time and won
        Assert.Empty(Feed(sut, T0, 0x30, 0xF6, 0x50, 0x22));
        Assert.Equal(1, sut.LostArbitrations);

        // its telegram goes on; only after an idle bus again the same request is retried
        Assert.Empty(Feed(sut, T0, 0x03, 0x22, 0x41, 0x06, 0x9C, 0x00, 0x02, 0x73, 0x00, 0x84, 0x00, 0xAA));
        Assert.Equal([0xFF], Feed(sut, T0, 0xAA));
    }

    [Fact]
    public void WrongEchoReleasesTheBus()
    {
        var sut = CreateSut(0x0641);
        Feed(sut, T0, 0xAA, 0xAA, 0xFF);

        Assert.Equal([0xAA], Feed(sut, T0, 0xF6, 0x51));
        Assert.Equal(1, sut.Failed);
    }

    [Fact]
    public void MissingAckReleasesTheBus()
    {
        var sut = CreateSut(0x0641);
        Feed(sut, T0, 0xAA, 0xAA, 0xFF);
        Feed(sut, T0, OutsideRequest[1..]);

        Assert.Equal([0xAA], Feed(sut, T0, 0xFF));
        Assert.Equal(1, sut.Failed);
    }

    [Fact]
    public void WrongAnswerCrcIsNacked()
    {
        var sut = CreateSut(0x0641);
        Feed(sut, T0, 0xAA, 0xAA, 0xFF);
        Feed(sut, T0, OutsideRequest[1..]);
        Feed(sut, T0, 0x00);

        Assert.Equal([0xFF, 0xAA], Feed(sut, T0, 0x02, 0x73, 0x00, 0x85));
        Assert.Equal(1, sut.Failed);
    }

    [Fact]
    public void NextCycleOnlyAfterTheInterval()
    {
        var sut = CreateSut(0x0641);
        Feed(sut, T0, 0xAA, 0xAA, 0xFF);
        Feed(sut, T0, OutsideRequest[1..]);
        Feed(sut, T0, 0x00, 0x02, 0x73, 0x00, 0x84);

        Assert.Empty(Feed(sut, T0.AddSeconds(59), 0xAA, 0xAA, 0xAA));
        Assert.Equal([0xFF], Feed(sut, T0.AddSeconds(61), 0xAA, 0xAA));
    }

    [Fact]
    public void StalledRequestTimesOut()
    {
        var sut = CreateSut(0x0641);
        Feed(sut, T0, 0xAA, 0xAA); // own address sent, echo never arrives

        Feed(sut, T0.AddSeconds(3), 0xAA);
        Assert.Equal(1, sut.Failed);
    }
}
