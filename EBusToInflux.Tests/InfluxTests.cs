using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EBusToInflux.Tests;

public class InfluxTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);

    // 0x0641 OutsideTemperature (measurement), 0x06BF RoomTargetHeating (setting), 0x064B VersionWpMajor (info)
    static Reading At(TimeSpan offset, ushort id, short raw) => new(T0 + offset, new WeishauptValue(0x22, id, raw));

    [Fact]
    public void PolicyWritesMeasurementsOnChangeAndRepeatsAfterInterval()
    {
        var sut = new RecordingPolicy(TimeSpan.FromMinutes(5));

        Assert.True(sut.ShouldWrite(At(TimeSpan.Zero, 0x0641, 115)));                    // first
        Assert.False(sut.ShouldWrite(At(TimeSpan.FromMinutes(1), 0x0641, 115)));         // unchanged
        Assert.True(sut.ShouldWrite(At(TimeSpan.FromMinutes(2), 0x0641, 116)));          // changed
        Assert.False(sut.ShouldWrite(At(TimeSpan.FromMinutes(6), 0x0641, 116)));         // 4 min since last write
        Assert.True(sut.ShouldWrite(At(TimeSpan.FromMinutes(7), 0x0641, 116)));          // 5 min since last write
    }

    [Theory]
    [InlineData(0x06BF)] // setting
    [InlineData(0x064B)] // info
    [InlineData(0x0199)] // unknown
    public void PolicySkipsEverythingButMeasurements(ushort id) =>
        Assert.False(new RecordingPolicy(TimeSpan.FromMinutes(5)).ShouldWrite(At(TimeSpan.Zero, id, 220)));

    [Fact]
    public void PolicySkipsNotAvailable() =>
        Assert.False(new RecordingPolicy(TimeSpan.FromMinutes(5)).ShouldWrite(At(TimeSpan.Zero, 0x0641, short.MinValue)));

    [Theory]
    [InlineData(0x0641, 115, "heatpump OutsideTemperature=11.5 1793469600")]
    [InlineData(0x0641, -30, "heatpump OutsideTemperature=-3 1793469600")]
    [InlineData(0x0650, 28890, "heatpump CompressorRuntime=28890 1793469600")]
    public void LineProtocolUsesScaledValueAndSeconds(ushort id, short raw, string expected) =>
        Assert.Equal(expected, LineProtocol.Format("heatpump", At(TimeSpan.Zero, id, raw)));

    [Fact]
    public void LineProtocolEscapesMeasurement() =>
        Assert.StartsWith(@"heat\ pump\,x OutsideTemperature=", LineProtocol.Format("heat pump,x", At(TimeSpan.Zero, 0x0641, 115)));

    [Fact]
    public async Task WriterPostsMeasurementsToInflux()
    {
        var handler = new RecordingHandler();
        var channel = Channel.CreateUnbounded<Reading>();
        var options = Options.Create(new InfluxOptions
        {
            Url = "http://influx.test:8086/",
            Org = "gacond",
            Bucket = "home",
            Token = "secret",
            FlushInterval = TimeSpan.FromMilliseconds(50),
        });
        var sut = new InfluxWriter(channel.Reader, new FakeHttpClientFactory(handler), options, NullLogger<InfluxWriter>.Instance);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        channel.Writer.TryWrite(At(TimeSpan.Zero, 0x0641, 115));
        channel.Writer.TryWrite(At(TimeSpan.Zero, 0x06BF, 220)); // setting, not written
        await handler.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await sut.StopAsync(TestContext.Current.CancellationToken);

        var (request, body) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://influx.test:8086/api/v2/write?org=gacond&bucket=home&precision=s", request.RequestUri!.ToString());
        Assert.Equal("Token secret", request.Headers.Authorization!.ToString());
        Assert.Equal("heatpump OutsideTemperature=11.5 1793469600", body);
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
        public TaskCompletionSource FirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(cancellationToken)));
            FirstRequest.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
