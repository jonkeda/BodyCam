using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Real-hardware test fixture for HeyCyan glasses.
/// 
/// NOTE: This fixture requires Android platform-specific implementations that are not
/// available in the Windows test host. Real hardware tests must be run on an Android
/// device/emulator with:
/// - HeyCyan glasses paired and powered on
/// - BODYCAM_REAL_HEYCYAN=1 environment variable set
/// - BODYCAM_REAL_HEYCYAN_MAC=XX:XX:XX:XX:XX:XX environment variable set
/// 
/// The tests will Skip.IfNot(RealEnabled) when the environment is not configured.
/// 
/// To run real hardware tests:
/// 1. Deploy BodyCam.RealTests to an Android device
/// 2. Set environment variables in the test runner
/// 3. Execute: dotnet test --filter "Category=RealHardware"
/// 
/// For development/CI, these tests are skipped by default.
/// </summary>
public sealed class HeyCyanRealFixture : IAsyncDisposable
{
    public IHeyCyanGlassesSession Session { get; }
    public IHeyCyanMediaTransfer Transfer { get; }
    public HeyCyanCameraProvider Camera { get; }

    private HeyCyanRealFixture(
        IHeyCyanGlassesSession session,
        IHeyCyanMediaTransfer transfer,
        HeyCyanCameraProvider camera)
    {
        Session = session;
        Transfer = transfer;
        Camera = camera;
    }

    public static Task<HeyCyanRealFixture> ConnectAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "HeyCyanRealFixture requires platform-specific Android implementations. " +
            "Real hardware tests must be run on an Android device with HeyCyan glasses paired. " +
            "In production, this would use the app's DI container with platform-specific registrations " +
            "from MauiProgram.Android.cs (IHeyCyanGlassesSession, IHeyCyanHttpClientFactory, etc.).");
    }

    public async ValueTask DisposeAsync()
    {
        await Session.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        await Session.DisposeAsync().ConfigureAwait(false);
        await Transfer.DisposeAsync().ConfigureAwait(false);
    }
}
