namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Optional platform hook for work that must start before the BLE transfer command.
/// Android Wi-Fi Direct discovery is one such case: the official HeyCyan app starts
/// discovery first, then asks the glasses over BLE to enter P2P album import mode.
/// </summary>
public interface IHeyCyanTransferPreparation
{
    Task PrepareForTransferAsync(CancellationToken ct);
}
