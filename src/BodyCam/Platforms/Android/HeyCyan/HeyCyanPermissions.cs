#if ANDROID
using Microsoft.Maui.ApplicationModel;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Handles runtime permission requests for HeyCyan SDK:
/// - BLUETOOTH_SCAN + BLUETOOTH_CONNECT (Android 12+)
/// - ACCESS_FINE_LOCATION (Android < 12, required for BLE scan results)
/// 
/// Phase 2 (WiFi-Direct transfer) will also require NEARBY_WIFI_DEVICES + CHANGE_WIFI_STATE,
/// but those are declared in the manifest now to avoid re-prompting the user later.
/// </summary>
internal static class HeyCyanPermissions
{
	/// <summary>
	/// Request all required permissions for HeyCyan BLE scan and connect.
	/// Throws HeyCyanPermissionException if the user denies any required permission.
	/// </summary>
	public static async Task RequestAsync()
	{
		// BLUETOOTH_SCAN + BLUETOOTH_CONNECT (Android 12+, API 31+)
		var bt = await Permissions.RequestAsync<Permissions.Bluetooth>().ConfigureAwait(false);
		if (bt != PermissionStatus.Granted)
			throw new HeyCyanPermissionException(nameof(Permissions.Bluetooth));

		// ACCESS_FINE_LOCATION (Android < 12) — BLE scan requires location on older OS versions
		if (OperatingSystem.IsAndroidVersionAtLeast(31) is false)
		{
			var loc = await Permissions.RequestAsync<Permissions.LocationWhenInUse>().ConfigureAwait(false);
			if (loc != PermissionStatus.Granted)
				throw new HeyCyanPermissionException(nameof(Permissions.LocationWhenInUse));
		}
	}
}

public sealed class HeyCyanPermissionException(string permission)
	: Exception($"User denied required permission: {permission}");
#endif
