namespace BodyCam.Services.Glasses.HeyCyan;

public sealed class NullHeyCyanAudioEndpointActivationService : IHeyCyanAudioEndpointActivationService
{
    private static readonly HeyCyanAudioEndpointSnapshot UnsupportedSnapshot = new(
        MacAddress: null,
        Summary: "HeyCyan audio endpoint activation is not required on this platform.",
        CaptureStatus: HeyCyanEndpointStatus.Unknown,
        RenderStatus: HeyCyanEndpointStatus.Unknown,
        CaptureEndpoints: [],
        RenderEndpoints: [],
        ProfileNodes: [],
        RequiresUserAction: false);

    public bool IsSupported => false;

    public bool RequiresActivationBeforeBleConnect => false;

    public HeyCyanAudioEndpointSnapshot? Current { get; private set; }

    public event EventHandler<HeyCyanAudioEndpointSnapshot>? Updated;

    public Task<HeyCyanAudioEndpointSnapshot> RefreshAsync(CancellationToken ct)
        => PublishAsync(UnsupportedSnapshot);

    public Task<HeyCyanAudioEndpointSnapshot> BeginActivationAsync(
        HeyCyanDeviceInfo? selectedDevice,
        CancellationToken ct)
        => PublishAsync(UnsupportedSnapshot);

    public Task OpenBluetoothSettingsAsync(CancellationToken ct) => Task.CompletedTask;

    private Task<HeyCyanAudioEndpointSnapshot> PublishAsync(HeyCyanAudioEndpointSnapshot snapshot)
    {
        Current = snapshot;
        Updated?.Invoke(this, snapshot);
        return Task.FromResult(snapshot);
    }
}
