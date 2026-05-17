namespace BodyCam.Pages;

[QueryProperty(nameof(Uri), "uri")]
public partial class AudioPlayerPage : ContentPage
{
    private string? _uri;

    public AudioPlayerPage()
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
                FileNameLabel.Text = System.IO.Path.GetFileName(decodedUri);
            }
        }
    }

    private async void OnPlayClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_uri))
            return;

        var decodedUri = System.Uri.UnescapeDataString(_uri);
        await Launcher.Default.OpenAsync(new OpenFileRequest
        {
            File = new ReadOnlyFile(GetLocalPath(decodedUri))
        });
    }

    private static string GetLocalPath(string uri)
    {
        // Handle content:// URIs (Android) or file:// URIs
        if (uri.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return System.Uri.UnescapeDataString(uri.Substring("file://".Length));
        }

        return uri;
    }
}
