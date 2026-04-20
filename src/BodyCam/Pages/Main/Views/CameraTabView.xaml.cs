using CommunityToolkit.Maui.Views;

namespace BodyCam.Pages.Main.Views;

public partial class CameraTabView : ContentView
{
    public CameraTabView()
    {
        InitializeComponent();
    }

    public CameraView CameraPreviewControl => CameraPreview;
    public Button DismissButton => DismissSnapshotButton;
}
