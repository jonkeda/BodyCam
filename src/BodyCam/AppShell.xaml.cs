namespace BodyCam;

public partial class AppShell : Shell
{
	private bool _checkedSetup;

	public AppShell(ViewModels.MainViewModel mainViewModel)
	{
		InitializeComponent();
		BindingContext = mainViewModel;
		Navigated += OnShellNavigated;

		Routing.RegisterRoute(nameof(Pages.Setup.SetupPage), typeof(Pages.Setup.SetupPage));
		Routing.RegisterRoute(nameof(Pages.Settings.SettingsPage), typeof(Pages.Settings.SettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.ConnectionSettingsPage), typeof(Pages.Settings.ConnectionSettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.VoiceSettingsPage), typeof(Pages.Settings.VoiceSettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.DeviceSettingsPage), typeof(Pages.Settings.DeviceSettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.AddDevicesPage), typeof(Pages.Settings.AddDevicesPage));
		Routing.RegisterRoute(nameof(Pages.Settings.A9CameraSettingsPage), typeof(Pages.Settings.A9CameraSettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.Vue990CameraSettingsPage), typeof(Pages.Settings.Vue990CameraSettingsPage));
#if WINDOWS
		Routing.RegisterRoute(nameof(Pages.Settings.UsbCameraSettingsPage), typeof(Pages.Settings.UsbCameraSettingsPage));
#endif
		Routing.RegisterRoute(nameof(Pages.Settings.AdvancedSettingsPage), typeof(Pages.Settings.AdvancedSettingsPage));
	}

	private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		if (!_checkedSetup)
		{
			_checkedSetup = true;
			var settings = Handler?.MauiContext?.Services.GetService<Services.ISettingsService>();
			if (settings is not null && !settings.SetupCompleted)
			{
				Dispatcher.Dispatch(async () =>
					await GoToAsync(nameof(Pages.Setup.SetupPage)));
				return;
			}
		}
	}
}
