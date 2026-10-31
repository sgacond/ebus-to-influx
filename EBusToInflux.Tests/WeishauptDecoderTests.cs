namespace EBusToInflux.Tests;

public class WeishauptDecoderTests
{
    [Fact]
    public void ReadResponseIsDecoded()
    {
        // 30 F6 50 22 03 22 41 06 -> 73 00 (outside temperature 11.5)
        var telegram = new Telegram(0x30, 0xF6, 0x50, 0x22, [0x22, 0x41, 0x06], 0, [0x73, 0x00]);

        var result = WeishauptDecoder.Decode(telegram);

        Assert.NotNull(result);
        Assert.Equal(0x0641, result.Id);
        Assert.Equal(115, result.Raw);
        Assert.Equal(11.5, result.Scaled);
        Assert.Equal("OutsideTemperature", WeishauptDecoder.NameOf(result.Id));
    }

    [Fact]
    public void WriteIsDecoded()
    {
        // 30 F1 50 23 09 88 B1 06 1C 02 5D 01 00 00 (hot water target 54.0)
        var telegram = new Telegram(0x30, 0xF1, 0x50, 0x23, [0x88, 0xB1, 0x06, 0x1C, 0x02, 0x5D, 0x01, 0x00, 0x00], 0);

        var result = WeishauptDecoder.Decode(telegram);

        Assert.NotNull(result);
        Assert.Equal(0x06B1, result.Id);
        Assert.Equal(540, result.Raw);
    }

    [Fact]
    public void NegativeValuesAreSigned()
    {
        var telegram = new Telegram(0x30, 0xF6, 0x50, 0x22, [0x22, 0x41, 0x06], 0, [0xE2, 0xFF]);

        Assert.Equal(-3.0, WeishauptDecoder.Decode(telegram)!.Scaled);
    }

    [Fact]
    public void NotAvailableIsDetected()
    {
        var telegram = new Telegram(0x30, 0xF6, 0x50, 0x22, [0x22, 0x41, 0x06], 0, [0x00, 0x80]);

        Assert.False(WeishauptDecoder.Decode(telegram)!.IsAvailable);
    }

    [Theory]
    [InlineData("224106")]             // read outside temperature
    [InlineData("98a806da005d010000")] // room temperature 21.8
    [InlineData("08a806db005d010000")] // room temperature 21.9, same id, other checksum
    [InlineData("f0a806df005d010000")] // room temperature 22.3
    public void ChecksumMatchesCapturedTelegrams(string hex) =>
        Assert.True(WeishauptDecoder.HasValidChecksum(Convert.FromHexString(hex)));

    [Fact]
    public void WrongChecksumIsRejected()
    {
        var telegram = new Telegram(0x30, 0xF6, 0x50, 0x22, [0x23, 0x41, 0x06], 0, [0x73, 0x00]);

        Assert.Null(WeishauptDecoder.Decode(telegram));
    }

    [Fact]
    public void OtherCommandsAreIgnored()
    {
        var telegram = new Telegram(0x10, 0xFE, 0x07, 0x00, [0x12, 0x34], 0);

        Assert.Null(WeishauptDecoder.Decode(telegram));
    }
}
