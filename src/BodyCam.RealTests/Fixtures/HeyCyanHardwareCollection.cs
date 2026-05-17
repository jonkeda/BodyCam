using BodyCam.Services.Glasses.HeyCyan;
using Xunit;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Shared fixture that creates the real HeyCyan hardware connection once
/// and shares it across all tests in the "HeyCyanHardware" collection.
/// This prevents rapid BLE connect/disconnect cycles that overwhelm the radio.
/// </summary>
public sealed class SharedHeyCyanFixture : IAsyncLifetime
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

        Inner = await WindowsHeyCyanRealFixture.CreateAsync();

        // Connect once — shared across all tests
        ConnectedDevice = await Inner.ConnectByAddressAsync(Mac, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (Inner != null)
            await Inner.DisposeAsync();
    }
}

[CollectionDefinition("HeyCyanHardware")]
public class HeyCyanHardwareCollection : ICollectionFixture<SharedHeyCyanFixture>
{
}
