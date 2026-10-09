using System.Net;
using System.Net.Http;
using System.Text;
using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Binance;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class BinanceDepthSnapshotClientTests
{
    [Fact]
    public async Task ParsesSnapshotUsingConfiguredPrecision()
    {
        var handler = new StubHandler("""{"lastUpdateId":100,"bids":[["65000.00","0.01000000"]],"asks":[["65001.00","0.02000000"]]}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.binance.test/") };
        var client = new BinanceDepthSnapshotClient(httpClient, Instrument());

        var snapshot = await client.GetSnapshotAsync("BTCUSDT", 500, CancellationToken.None);

        Assert.Equal(100, snapshot.LastUpdateId);
        Assert.Equal(6500000000000, Assert.Single(snapshot.Bids).Price.Units);
        Assert.Equal(2000000, Assert.Single(snapshot.Asks).Quantity.Units);
        Assert.Equal("/api/v3/depth?symbol=BTCUSDT&limit=500", handler.RequestUri);
    }

    [Fact]
    public async Task RejectsInvalidSnapshotPayload()
    {
        var handler = new StubHandler("""{"lastUpdateId":100,"bids":[["65000.001","0.01"]],"asks":[]}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.binance.test/") };
        var client = new BinanceDepthSnapshotClient(httpClient, Instrument());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetSnapshotAsync("BTCUSDT", 500, CancellationToken.None));
    }

    private static VenueInstrument Instrument() =>
        new(Exchange.BinanceSpot, "BTCUSDT", new VenuePrecision(8, 1_000_000, 8, 1_000));

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
