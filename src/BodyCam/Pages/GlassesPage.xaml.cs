using BodyCam.ViewModels;

namespace BodyCam.Pages;

public partial class GlassesPage : ContentPage
{
    public GlassesPage(GlassesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
