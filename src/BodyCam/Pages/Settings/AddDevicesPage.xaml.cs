using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class AddDevicesPage : ContentPage
{
    public AddDevicesPage(AddDevicesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
