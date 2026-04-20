using BodyCam.ViewModels;

namespace BodyCam.Pages.Setup;

public partial class SetupPage : ContentPage
{
	public SetupPage(SetupViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
		viewModel.SetupFinished += OnSetupFinished;
	}

	private async void OnSetupFinished(object? sender, EventArgs e)
	{
		if (sender is SetupViewModel vm)
			vm.SetupFinished -= OnSetupFinished;

		await Shell.Current.GoToAsync("..");
	}
}
