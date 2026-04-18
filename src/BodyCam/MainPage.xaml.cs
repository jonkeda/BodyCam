using BodyCam.Helpers;
using BodyCam.Services.Audio;
using BodyCam.Services.Input;
using BodyCam.Services.Camera;
using BodyCam.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace BodyCam;

public partial class MainPage : ContentPage
{
	private bool _isNearBottom = true;

	public MainPage(MainViewModel viewModel, PhoneCameraProvider phoneCamera, AudioInputManager audioInputManager, AudioOutputManager audioOutputManager, ButtonInputManager buttonInput, IServiceProvider services)
	{
		InitializeComponent();
		BindingContext = viewModel;
		phoneCamera.SetCameraView(CameraPreview);

		TranscriptList.Scrolled += (_, e) =>
		{
			// If the last visible item index is at or near the end, we're "at the bottom"
			_isNearBottom = e.LastVisibleItemIndex >= ((BindingContext as MainViewModel)?.Entries.Count ?? 1) - 2;
		};

		Loaded += async (_, _) =>
		{
			await audioInputManager.InitializeAsync();
			await audioOutputManager.InitializeAsync();
			await buttonInput.StartAsync();

			// Scan for Bluetooth audio devices after audio manager is ready
#if WINDOWS
			var btEnum = services.GetService<BodyCam.Platforms.Windows.Audio.WindowsBluetoothEnumerator>();
			btEnum?.ScanAndRegister();
			btEnum?.StartListening();

			var btOutEnum = services.GetService<BodyCam.Platforms.Windows.Audio.WindowsBluetoothOutputEnumerator>();
			btOutEnum?.ScanAndRegister();
			btOutEnum?.StartListening();
#elif ANDROID
			var btEnum = services.GetService<BodyCam.Platforms.Android.Audio.AndroidBluetoothEnumerator>();
			btEnum?.ScanAndRegister();
			btEnum?.StartListening();

			var btOutEnum = services.GetService<BodyCam.Platforms.Android.Audio.AndroidBluetoothOutputEnumerator>();
			btOutEnum?.ScanAndRegister();
			btOutEnum?.StartListening();
#endif
		};

		if (BindingContext is MainViewModel vm)
		{
			vm.Entries.CollectionChanged += (_, e) =>
			{
				if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
					&& vm.Entries.Count > 0
					&& _isNearBottom)
				{
					TranscriptList.ScrollTo(vm.Entries.Count - 1, position: ScrollToPosition.End, animate: false);
				}
			};

			vm.PropertyChanged += (_, e) =>
			{
				if (e.PropertyName == nameof(MainViewModel.ShowSnapshot) && vm.ShowSnapshot)
					DismissSnapshotButton.Focus();
			};
		}
	}

	private async void EntryItem_Loaded(object sender, EventArgs e)
	{
		if (sender is not VisualElement element) return;

		if (MotionPreference.PrefersReducedMotion)
		{
			element.Opacity = 1;
			element.TranslationY = 0;
			return;
		}

		await Task.WhenAll(
			element.FadeTo(1, 250, Easing.CubicOut),
			element.TranslateTo(0, 0, 250, Easing.CubicOut));
	}

	private async void ThinkingDots_Loaded(object sender, EventArgs e)
	{
		if (sender is not HorizontalStackLayout layout) return;

		var dots = layout.Children.OfType<Ellipse>().ToList();
		if (dots.Count < 3) return;

		if (MotionPreference.PrefersReducedMotion)
		{
			foreach (var dot in dots)
				dot.Opacity = 1.0;
			return;
		}

		while (layout.IsVisible)
		{
			for (int i = 0; i < dots.Count; i++)
			{
				await dots[i].FadeTo(1.0, 200);
				await dots[i].FadeTo(0.3, 200);
			}
			await Task.Delay(100);
		}
	}
}
