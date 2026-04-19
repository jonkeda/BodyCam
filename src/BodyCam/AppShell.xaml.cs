namespace BodyCam;

public partial class AppShell : Shell
{
	private bool _onSettings;
	private bool _checkedSetup;

	public AppShell()
	{
		InitializeComponent();
		Navigated += OnShellNavigated;

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
		_onSettings = loc.Contains("SettingsPage");
		NavIcon.Text = _onSettings ? "✕" : "⚙";

		if (!_checkedSetup)
		{
			_checkedSetup = true;
			var settings = Handler?.MauiContext?.Services.GetService<Services.ISettingsService>();
			if (settings is not null && !settings.SetupCompleted)
			{
				// Defer to avoid "Pending Navigations still processing" during Shell init
				Dispatcher.Dispatch(async () => await GoToAsync("//SetupPage"));
				return;
			}
			if (loc.Contains("SetupPage"))
			{
				Dispatcher.Dispatch(async () => await GoToAsync("//MainPage"));
			}
		}
	}

	private async void OnNavIconTapped(object? sender, EventArgs e)
	{
		if (_onSettings)
			await Current.GoToAsync("//MainPage");
		else
			await Current.GoToAsync("//SettingsPage");
	}
}
