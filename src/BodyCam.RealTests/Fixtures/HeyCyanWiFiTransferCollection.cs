using BodyCam.Services.Glasses.HeyCyan;
using Xunit;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Shared fixture that creates the real HeyCyan hardware connection with
/// WiFi + HTTP media transfer support once and shares it across all tests
/// in the "HeyCyanWiFiTransfer" collection. Prevents rapid BLE
/// connect/disconnect cycles that overwhelm the radio.
/// </summary>
public sealed class SharedHeyCyanWiFiFixture : IAsyncLifetime
{
    public WindowsHeyCyanRealFixture? Inner { get; private set; }
    public HeyCyanDeviceInfo? ConnectedDevice { get; private set; }

    public bool IsEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    public string Mac =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC") ?? "";

    public async Task InitializeAsync()
    {
        if (!IsEnabled || string.IsNullOrEmpty(Mac)) return;

        Inner = await WindowsHeyCyanRealFixture.CreateWithTransferAsync();
        ConnectedDevice = await Inner.ConnectByAddressAsync(Mac, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (Inner != null)
            await Inner.DisposeAsync();
    }
}

[CollectionDefinition("HeyCyanWiFiTransfer")]
public class HeyCyanWiFiTransferCollection : ICollectionFixture<SharedHeyCyanWiFiFixture>
{
}
