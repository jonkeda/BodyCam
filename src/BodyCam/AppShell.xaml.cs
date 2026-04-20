namespace BodyCam;

public partial class AppShell : Shell
{
	private bool _checkedSetup;

	public AppShell()
	{
		InitializeComponent();
		Navigated += OnShellNavigated;

		Routing.RegisterRoute(nameof(Pages.Setup.SetupPage), typeof(Pages.Setup.SetupPage));
		Routing.RegisterRoute(nameof(Pages.Settings.SettingsPage), typeof(Pages.Settings.SettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.ConnectionSettingsPage), typeof(Pages.Settings.ConnectionSettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.VoiceSettingsPage), typeof(Pages.Settings.VoiceSettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.DeviceSettingsPage), typeof(Pages.Settings.DeviceSettingsPage));
		Routing.RegisterRoute(nameof(Pages.Settings.AdvancedSettingsPage), typeof(Pages.Settings.AdvancedSettingsPage));

		var buildNumber = typeof(AppShell).Assembly
			.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
			.OfType<System.Reflection.AssemblyMetadataAttribute>()
			.FirstOrDefault(a => a.Key == "BuildNumber")?.Value;
		if (!string.IsNullOrEmpty(buildNumber))
			BuildLabel.Text = $"{buildNumber}";
	}

	private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		var loc = e.Current?.Location?.OriginalString ?? "";

		// Show ⚙ only on MainPage root; pushed pages use Shell back arrow
		// Root: "//MainPage", pushed: "//MainPage/SettingsPage"
		var segments = loc.Trim('/').Split('/');
		NavIcon.IsVisible = segments.Length <= 1;

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

	private async void OnNavIconTapped(object? sender, EventArgs e)
	{
		await Current.GoToAsync(nameof(Pages.Settings.SettingsPage));
	}
}
