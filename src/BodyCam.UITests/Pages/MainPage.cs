namespace BodyCam.UITests.Pages;

public class MainPage : PageObjectBase<MainPage>
{
    public MainPage(IMauiTestContext context) : base(context) { }

    public override string Name => "MainPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => TranscriptTabButton.WaitExists(true, timeoutMs);

    // Shell navigation
    public Button<MainPage> NavIcon => Button("NavIcon");

    // Status bar
    public Button<MainPage> OffButton => Button("OffButton");
    public Button<MainPage> OnButton => Button("OnButton");
    public Button<MainPage> ListeningButton => Button("ListeningButton");
    public Button<MainPage> ClearButton => Button("ClearButton");

    // Tab switcher
    public Button<MainPage> TranscriptTabButton => Button("TranscriptTabButton");
    public Button<MainPage> CameraTabButton => Button("CameraTabButton");

    // Quick actions
    public Button<MainPage> LookButton => Button("LookButton");
    public Button<MainPage> ReadButton => Button("ReadButton");
    public Button<MainPage> FindButton => Button("FindButton");
    public Button<MainPage> AskButton => Button("AskButton");
    public Button<MainPage> PhotoButton => Button("PhotoButton");

    // Content panels (using child elements as sentinels since MAUI Grid/Frame don't create UIA peers)
    public Label<MainPage> CameraPlaceholder => Label("CameraPlaceholder");

    // Debug overlay
    public Label<MainPage> DebugLabel => Label("DebugLabel");

    // Snapshot overlay
    public Label<MainPage> SnapshotCaption => Label("SnapshotCaption");
    public Button<MainPage> DismissSnapshotButton => Button("DismissSnapshotButton");
}
