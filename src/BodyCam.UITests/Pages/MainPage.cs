namespace BodyCam.UITests.Pages;

public class MainPage : PageObjectBase<MainPage>
{
    public MainPage(IMauiTestContext context) : base(context) { }

    public override string Name => "MainPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => TranscriptList.WaitExists(true, timeoutMs);

    // First-page navigation
    public Button<MainPage> SettingsButton => Button("SettingsButton");
    public Button<MainPage> NavIcon => SettingsButton;

    // Status bar
    public Button<MainPage> OffButton => Button("OffButton");
    public Button<MainPage> OnButton => Button("OnButton");
    public Button<MainPage> ListeningButton => Button("ListeningButton");
    public Button<MainPage> SpeakButton => Button("SpeakButton");
    public Button<MainPage> SilentButton => Button("SilentButton");

    // Legacy tab switcher sentinels
    public Button<MainPage> TranscriptTabButton => Button("TranscriptTabButton");
    public Button<MainPage> CameraTabButton => Button("CameraTabButton");

    // Quick actions
    public Button<MainPage> ActionsDrawerButton => Button("ActionsDrawerButton");
    public Button<MainPage> LookButton => Button("LookButton");
    public Button<MainPage> ReadButton => Button("ReadButton");
    public Button<MainPage> ScanButton => Button("ScanButton");

    // Content panels
    public Brinell.Maui.Controls.Collection.CollectionView<MainPage> TranscriptList => CollectionView("TranscriptList");
    public Entry<MainPage> MessageEntry => Entry("MessageEntry");
    public Button<MainPage> SendMessageButton => Button("SendMessageButton");
    public Brinell.Maui.Controls.Container.Grid<MainPage> CameraPreviewPanel => Grid("CameraPreviewPanel");
    public Button<MainPage> CaptureFrameButton => Button("CaptureFrameButton");

    // Debug overlay
    public Label<MainPage> DebugLabel => Label("DebugLabel");
    public Label<MainPage> AudioPolicyDebugLabel => Label("AudioPolicyDebugLabel");

    // Snapshot overlay
    public Label<MainPage> SnapshotCaption => Label("SnapshotCaption");
    public Button<MainPage> DismissSnapshotButton => Button("DismissSnapshotButton");

    public void EnsureActionsExpanded()
    {
        if (!LookButton.WaitExists(true, 1000))
            ActionsDrawerButton.Click();

        LookButton.WaitExists(true, 5000);
    }
}
