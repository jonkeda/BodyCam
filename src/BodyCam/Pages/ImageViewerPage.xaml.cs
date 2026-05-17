namespace BodyCam.Pages;

[QueryProperty(nameof(Uri), "uri")]
public partial class ImageViewerPage : ContentPage
{
    private string? _uri;

    public ImageViewerPage()
    {
        InitializeComponent();
    }

    public string? Uri
    {
        get => _uri;
        set
        {
            _uri = value;
            if (!string.IsNullOrEmpty(_uri))
            {
                var decodedUri = System.Uri.UnescapeDataString(_uri);
                ImageControl.Source = ImageSource.FromUri(new System.Uri(decodedUri));
            }
        }
    }
}
