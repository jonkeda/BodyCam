using BodyCam.ViewModels;

namespace BodyCam.Pages;

public partial class MediaGalleryPage : ContentPage
{
    public MediaGalleryPage(MediaGalleryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
