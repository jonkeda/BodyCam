namespace BodyCam;

public partial class AppShell : Shell
{
	private bool _onSettings;

	public AppShell()
	{
		InitializeComponent();
		Navigated += OnShellNavigated;
	}

	private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		_onSettings = e.Current?.Location?.OriginalString?.Contains("SettingsPage") == true;
		NavIcon.Text = _onSettings ? "✕" : "⚙";
	}

	private async void OnNavIconTapped(object? sender, EventArgs e)
	{
		if (_onSettings)
			await Current.GoToAsync("//MainPage");
		else
			await Current.GoToAsync("//SettingsPage");
	}
}
