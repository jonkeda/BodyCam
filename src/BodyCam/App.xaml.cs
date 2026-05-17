namespace BodyCam;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		_services = services;
		InitializeComponent();

#if WINDOWS
		// Eagerly warm the paired BT device cache so it's available before any
		// connection attempt or BT enumerator scan (needed for Intel SST fallback).
		_ = Platforms.Windows.Audio.WindowsBluetoothEnumerator.RefreshPairedDeviceCacheAsync();
#endif
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var shell = _services.GetRequiredService<AppShell>();
		return new Window(shell);
	}
}