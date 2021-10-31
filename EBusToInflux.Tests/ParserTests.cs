using System.Collections.Generic;
using Xunit;

namespace EBusToInflux.Tests
{
    public class ParserTests
    {
        [Fact]
        public void OnlyAAsDoNothing()
        {
            var sut = new TelegramParser();
            var bytes = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA };

            foreach (var b in bytes)
            {
                var result = sut.ContinueParse(b);
                Assert.Null(result);
            }
        }

        [Fact]
        public void PlainPackageWithoutResponseWorks()
        {
            var sut = new TelegramParser();
            var bytes = new byte[] { 0x30, 0xF1, 0x50, 0x23, 0x09, 0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00, 0xDE, 0x00, 0xAA };

            Telegram? result = null;
            foreach (var b in bytes)
                result = sut.ContinueParse(b);

            Assert.NotNull(result);
            Assert.Equal(0x30, result!.Sender);
            Assert.Equal(0xF1, result!.Receiver);
            Assert.Equal(0x50, result!.PrimaryCommand);
            Assert.Equal(0x23, result!.SecondaryCommand);
            Assert.Equal(new List<byte> { 0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00 }, result!.Payload);
            Assert.Equal(0xDE, result!.CRC);
            Assert.Null(result!.ResponsePayload);
        }

        [Fact]
        public void PlainPackageWithResponseWorks()
        {
            var sut = new TelegramParser();
            var bytes = new byte[] { 0x30, 0xF6, 0x50, 0x22, 0x03, 0xA6, 0x63, 0x06, 0x22, 0x00, 0x02, 0x00, 0x00, 0x2C, 0x00 };

            Telegram? result = null;
            foreach (var b in bytes)
                result = sut.ContinueParse(b);

            Assert.NotNull(result);
            Assert.Equal(0x30, result!.Sender);
            Assert.Equal(0xF6, result!.Receiver);
            Assert.Equal(0x50, result!.PrimaryCommand);
            Assert.Equal(0x22, result!.SecondaryCommand);
            Assert.Equal(new List<byte> { 0xA6, 0x63, 0x06 }, result!.Payload);
            Assert.Equal(0x22, result!.CRC);
            Assert.Equal(new List<byte> { 0x00, 0x00 }, result!.ResponsePayload);
            Assert.Equal(0x2C, result!.ResponseCRC);
        }

        [Fact]
        public void MultiInARow()
        {
            var sut = new TelegramParser();
            var bytes = new byte[] { 0x30, 0xF6, 0x50, 0x22, 0x03, 0xA6, 0x63, 0x06, 0x22, 0x00, 0x02, 0x00, 0x00, 0x2C, 0x00 };

            Telegram? result = null;
            foreach (var b in bytes)
                result = sut.ContinueParse(b);

            var bytes2 = new byte[] { 0x30, 0xF1, 0x50, 0x23, 0x09, 0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00, 0xDE, 0x00, 0xAA };

            Telegram? result2 = null;
            foreach (var b in bytes2)
                result2 = sut.ContinueParse(b);

            Assert.NotNull(result);
            Assert.Equal(0x30, result!.Sender);
            Assert.Equal(0xF6, result!.Receiver);
            Assert.Equal(0x50, result!.PrimaryCommand);
            Assert.Equal(0x22, result!.SecondaryCommand);
            Assert.Equal(new List<byte> { 0xA6, 0x63, 0x06 }, result!.Payload);
            Assert.Equal(0x22, result!.CRC);
            Assert.Equal(new List<byte> { 0x00, 0x00 }, result!.ResponsePayload);
            Assert.Equal(0x2C, result!.ResponseCRC);

            Assert.NotNull(result2);
            Assert.Equal(0x30, result2!.Sender);
            Assert.Equal(0xF1, result2!.Receiver);
            Assert.Equal(0x50, result2!.PrimaryCommand);
            Assert.Equal(0x23, result2!.SecondaryCommand);
            Assert.Equal(new List<byte> { 0x44, 0x85, 0x02, 0x50, 0x00, 0x5D, 0x01, 0x00, 0x00 }, result2!.Payload);
            Assert.Equal(0xDE, result2!.CRC);
            Assert.Null(result2!.ResponsePayload);
        }
    }
}