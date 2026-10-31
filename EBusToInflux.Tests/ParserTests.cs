namespace EBusToInflux.Tests;

public class ParserTests
{
    /// <summary>Feeds all bytes and returns the result of the last one.</summary>
    static Telegram? ParseLast(TelegramParser parser, params ReadOnlySpan<byte> bytes)
    {
        Telegram? result = null;
        foreach (var b in bytes)
            result = parser.ContinueParse(b);
        return result;
    }

    [Fact]
    public void OnlyAAsDoNothing()
    {
        var sut = new TelegramParser();

        foreach (var b in new byte[] { 0xAA, 0xAA, 0xAA, 0xAA })
            Assert.Null(sut.ContinueParse(b));
    }

    [Fact]
    public void PlainPackageWithoutResponseWorks()
    {
        var result = ParseLast(new TelegramParser(),
            0x30, 0xF1, 0x50, 0x23, 0x09, 0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00, 0xDE, 0x00, 0xAA);

        Assert.NotNull(result);
        Assert.Equal(0x30, result.Sender);
        Assert.Equal(0xF1, result.Receiver);
        Assert.Equal(0x50, result.PrimaryCommand);
        Assert.Equal(0x23, result.SecondaryCommand);
        Assert.Equal([0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00], result.Payload);
        Assert.Equal(0xDE, result.Crc);
        Assert.Null(result.ResponsePayload);
    }

    [Fact]
    public void PlainPackageWithResponseWorks()
    {
        var result = ParseLast(new TelegramParser(),
            0x30, 0xF6, 0x50, 0x22, 0x03, 0xA6, 0x63, 0x06, 0x22, 0x00, 0x02, 0x00, 0x00, 0x2C, 0x00);

        Assert.NotNull(result);
        Assert.Equal(0x30, result.Sender);
        Assert.Equal(0xF6, result.Receiver);
        Assert.Equal(0x50, result.PrimaryCommand);
        Assert.Equal(0x22, result.SecondaryCommand);
        Assert.Equal([0xA6, 0x63, 0x06], result.Payload);
        Assert.Equal(0x22, result.Crc);
        Assert.Equal([0x00, 0x00], result.ResponsePayload);
        Assert.Equal(0x2C, result.ResponseCrc);
    }

    [Fact]
    public void MultiInARow()
    {
        var sut = new TelegramParser();

        var result = ParseLast(sut,
            0x30, 0xF6, 0x50, 0x22, 0x03, 0xA6, 0x63, 0x06, 0x22, 0x00, 0x02, 0x00, 0x00, 0x2C, 0x00);
        var result2 = ParseLast(sut,
            0x30, 0xF1, 0x50, 0x23, 0x09, 0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00, 0xDE, 0x00, 0xAA);

        Assert.NotNull(result);
        Assert.Equal(0xF6, result.Receiver);
        Assert.Equal([0xA6, 0x63, 0x06], result.Payload);
        Assert.Equal([0x00, 0x00], result.ResponsePayload);
        Assert.Equal(0x2C, result.ResponseCrc);

        Assert.NotNull(result2);
        Assert.Equal(0xF1, result2.Receiver);
        Assert.Equal([0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00], result2.Payload);
        Assert.Equal(0xDE, result2.Crc);
        Assert.Null(result2.ResponsePayload);
    }

    [Fact]
    public void EscapedBytesAreUnstuffed()
    {
        var sut = new TelegramParser();

        // payload A9 AA is transmitted as A9 00 A9 01
        var result = ParseLast(sut, 0xAA, 0x30, 0xF1, 0x50, 0x23, 0x02, 0xA9, 0x00, 0xA9, 0x01, 0x7D, 0x00, 0xAA);

        Assert.NotNull(result);
        Assert.Equal([0xA9, 0xAA], result.Payload);
        Assert.Equal(0x7D, result.Crc);
        Assert.Equal(0, sut.ErrorCount);
    }

    [Fact]
    public void InvalidEscapeIsDiscarded()
    {
        var sut = new TelegramParser();

        var result = ParseLast(sut, 0xAA, 0x30, 0xF1, 0x50, 0x23, 0x02, 0xA9, 0xA9, 0x00, 0x7D, 0x00, 0xAA);

        Assert.Null(result);
        Assert.Equal(1, sut.ErrorCount);
    }

    [Fact]
    public void BroadcastWithoutAckWorks()
    {
        var result = ParseLast(new TelegramParser(), 0xAA, 0x10, 0xFE, 0x07, 0x00, 0x02, 0x12, 0x34, 0x46, 0xAA);

        Assert.NotNull(result);
        Assert.Equal(0xFE, result.Receiver);
        Assert.Equal([0x12, 0x34], result.Payload);
        Assert.Null(result.ResponsePayload);
    }

    [Fact]
    public void WrongCrcIsDiscardedAndParserResyncs()
    {
        var sut = new TelegramParser();

        // CRC corrupted (0xDF instead of 0xDE), followed by garbage before the next SYN
        Assert.Null(ParseLast(sut,
            0x30, 0xF1, 0x50, 0x23, 0x09, 0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00, 0xDF, 0x00, 0x12, 0x34));
        var result = ParseLast(sut,
            0xAA, 0x30, 0xF1, 0x50, 0x23, 0x09, 0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00, 0xDE, 0x00, 0xAA);

        Assert.Equal(1, sut.ErrorCount);
        Assert.NotNull(result);
        Assert.Equal(0xDE, result.Crc);
    }

    [Fact]
    public void TruncatedTelegramIsDiscardedOnSyn()
    {
        var sut = new TelegramParser();

        foreach (var b in new byte[] { 0x30, 0xF6, 0x50, 0x22, 0x03, 0xA6, 0xAA })
            Assert.Null(sut.ContinueParse(b));

        Assert.Equal(1, sut.ErrorCount);
    }

    [Theory]
    [InlineData(0x30, true)]
    [InlineData(0xF1, true)]
    [InlineData(0x10, true)]
    [InlineData(0xF6, false)]
    [InlineData(0x08, false)]
    [InlineData(0xFE, false)]
    public void MasterAddressDetection(byte address, bool isMaster) =>
        Assert.Equal(isMaster, TelegramParser.IsMasterAddress(address));
}
