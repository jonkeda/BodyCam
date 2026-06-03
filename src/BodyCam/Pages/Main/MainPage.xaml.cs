using BodyCam.Helpers;
using BodyCam.Services;
using BodyCam.Services.Input;
using BodyCam.Services.Camera;
using BodyCam.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace BodyCam.Pages.Main;

public partial class MainPage : ContentPage
{
	private bool _isNearBottom = true;

	public MainPage(
		MainViewModel viewModel,
		PhoneCameraProvider phoneCamera,
		ButtonInputManager buttonInput,
		IAppRuntimeCoordinator runtimeCoordinator)
	{
		InitializeComponent();
		BindingContext = viewModel;
		phoneCamera.SetCameraView(CameraPanel.CameraPreviewControl);
		viewModel.SetCameraView(CameraPanel.CameraPreviewControl);

		TranscriptPanel.List.Scrolled += (_, e) =>
		{
			// If the last visible item index is at or near the end, we're "at the bottom"
			_isNearBottom = e.LastVisibleItemIndex >= ((BindingContext as MainViewModel)?.Entries.Count ?? 1) - 2;
		};

		Loaded += async (_, _) =>
		{
			await runtimeCoordinator.StartAsync();
		};

		if (BindingContext is MainViewModel vm)
		{
			buttonInput.ActionTriggered += (_, action) =>
				Dispatcher.Dispatch(async () => await vm.HandleButtonActionAsync(action));

			vm.Entries.CollectionChanged += (_, e) =>
			{
				if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
					&& vm.Entries.Count > 0
					&& _isNearBottom)
				{
					// Dispatch to let CollectionView finish layout before scrolling
					Dispatcher.Dispatch(() =>
					{
						TranscriptPanel.List.ScrollTo(vm.Entries.Count - 1, position: ScrollToPosition.End, animate: false);
					});
				}
			};

			vm.PropertyChanged += (_, e) =>
			{
				if (e.PropertyName == nameof(MainViewModel.ShowSnapshot) && vm.ShowSnapshot)
					CameraPanel.DismissButton.Focus();
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
			element.FadeToAsync(1, 250, Easing.CubicOut),
			element.TranslateToAsync(0, 0, 250, Easing.CubicOut));
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
				await dots[i].FadeToAsync(1.0, 200);
				await dots[i].FadeToAsync(0.3, 200);
			}
			await Task.Delay(100);
		}
	}
}
