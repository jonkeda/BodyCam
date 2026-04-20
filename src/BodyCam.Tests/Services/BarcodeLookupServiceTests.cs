using BodyCam.Services.Barcode;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class BarcodeLookupServiceTests
{
    private sealed class StubClient : IBarcodeApiClient
    {
        private readonly ProductInfo? _result;
        public int CallCount { get; private set; }

        public StubClient(ProductInfo? result) => _result = result;

        public Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    [Fact]
    public async Task LookupAsync_ReturnsFirstClientResult()
    {
        var product = new ProductInfo { Barcode = "123", Source = "test", Name = "Found" };
        var client1 = new StubClient(product);
        var client2 = new StubClient(null);

        var service = new BarcodeLookupService([client1, client2]);
        var result = await service.LookupAsync("123");

        result.Should().BeSameAs(product);
        client1.CallCount.Should().Be(1);
        client2.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task LookupAsync_FallsThrough_WhenFirstReturnsNull()
    {
        var product = new ProductInfo { Barcode = "123", Source = "second", Name = "Fallback" };
        var client1 = new StubClient(null);
        var client2 = new StubClient(product);

        var service = new BarcodeLookupService([client1, client2]);
        var result = await service.LookupAsync("123");

        result.Should().BeSameAs(product);
        client1.CallCount.Should().Be(1);
        client2.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenAllFail()
    {
        var client1 = new StubClient(null);
        var client2 = new StubClient(null);

        var service = new BarcodeLookupService([client1, client2]);
        var result = await service.LookupAsync("123");

        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_CachesResult()
    {
        var product = new ProductInfo { Barcode = "123", Source = "test", Name = "Cached" };
        var client = new StubClient(product);

        var service = new BarcodeLookupService([client]);

        var first = await service.LookupAsync("123");
        var second = await service.LookupAsync("123");

        first.Should().BeSameAs(second);
        client.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task LookupAsync_DifferentBarcodes_QueriesSeparately()
    {
        var product = new ProductInfo { Barcode = "123", Source = "test", Name = "A" };
        var client = new StubClient(product);

        var service = new BarcodeLookupService([client]);

        await service.LookupAsync("123");
        await service.LookupAsync("456");

        client.CallCount.Should().Be(2);
    }
}
