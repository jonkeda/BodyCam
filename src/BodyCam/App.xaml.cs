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
		var window = new Window(shell);
#if WINDOWS
		window.Title = BuildWindowTitle();
#endif
		return window;
	}

	private static string BuildWindowTitle()
	{
		var version = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;
		if (string.IsNullOrWhiteSpace(version))
			version = typeof(App).Assembly.GetName().Version?.ToString() ?? string.Empty;

		var buildNumber = typeof(App).Assembly
			.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
			.OfType<System.Reflection.AssemblyMetadataAttribute>()
			.FirstOrDefault(a => a.Key == "BuildNumber")?.Value;

		if (!string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(buildNumber))
			return $"BodyCam {version} ({buildNumber})";

		if (!string.IsNullOrWhiteSpace(version))
			return $"BodyCam {version}";

		return "BodyCam";
	}
}
