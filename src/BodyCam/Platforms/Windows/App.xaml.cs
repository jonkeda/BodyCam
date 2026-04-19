using Microsoft.UI.Xaml;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BodyCam.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		// Catch WinUI XAML unhandled exceptions
		this.UnhandledException += (sender, e) =>
		{
			var msg = $"[WinUI UnhandledException] {e.Exception?.GetType().Name}: {e.Exception?.Message}\n{e.Exception?.StackTrace}";
			if (e.Exception?.InnerException is { } inner)
				msg += $"\n--- Inner: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}";
			Debug.WriteLine(msg);
			System.IO.File.AppendAllText(
				System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BodyCam_crash.log"),
				$"\n[{DateTime.Now:O}] {msg}\n");
			e.Handled = true; // Prevent immediate crash so log is flushed
		};

		// Catch .NET unhandled exceptions
		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			var ex = e.ExceptionObject as Exception;
			var msg = $"[AppDomain UnhandledException] {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}";
			Debug.WriteLine(msg);
			System.IO.File.AppendAllText(
				System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BodyCam_crash.log"),
				$"\n[{DateTime.Now:O}] {msg}\n");
		};

		// Catch unobserved task exceptions
		TaskScheduler.UnobservedTaskException += (sender, e) =>
		{
			var msg = $"[UnobservedTaskException] {e.Exception?.GetType().Name}: {e.Exception?.Message}\n{e.Exception?.StackTrace}";
			Debug.WriteLine(msg);
			System.IO.File.AppendAllText(
				System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BodyCam_crash.log"),
				$"\n[{DateTime.Now:O}] {msg}\n");
			e.SetObserved();
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

