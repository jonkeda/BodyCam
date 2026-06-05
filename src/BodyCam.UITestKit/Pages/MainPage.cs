namespace BodyCam.UITestKit.Pages;

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
    public Button<MainPage> SleepButton => OffButton;
    public Button<MainPage> ListenButton => OnButton;
    public Button<MainPage> ActiveButton => ListeningButton;
    public Button<MainPage> SpeakButton => Button("SpeakButton");
    public Button<MainPage> SilentButton => Button("SilentButton");

    // Legacy tab switcher sentinels
    public Button<MainPage> TranscriptTabButton => Button("TranscriptTabButton");
    public Button<MainPage> CameraTabButton => Button("CameraTabButton");

    // Quick actions
    [UatName("Actions Drawer Button")]
    public Button<MainPage> ActionsDrawerButton => Button("ActionsDrawerButton");
    public Brinell.Maui.Controls.Container.Grid<MainPage> ActionsDrawer => Grid("ActionsDrawer");
    public Button<MainPage> LookButton => Button("LookButton");
    public Button<MainPage> FindButton => Button("FindButton");
    public Button<MainPage> ReadButton => Button("ReadButton");
    public Button<MainPage> ScanButton => Button("ScanButton");
    public Button<MainPage> ProductButton => Button("ProductLookupButton");

    // Content panels
    public Brinell.Maui.Controls.Collection.CollectionView<MainPage> TranscriptList => CollectionView("TranscriptList");
    [UatName("Transcript You Entry")]
    public Label<MainPage> TranscriptYouEntry => Label("TranscriptYouEntryLabel");

    [UatName("Transcript AI Entry")]
    public Label<MainPage> TranscriptAiEntry => Label("TranscriptAiEntryLabel");

    [UatName("Transcript Image Caption")]
    public Label<MainPage> TranscriptImageCaption => Label("TranscriptImageCaptionLabel");

    public Entry<MainPage> MessageEntry => Entry("MessageEntry");
    public Button<MainPage> SendMessageButton => Button("SendMessageButton");
    public Brinell.Maui.Controls.Container.Grid<MainPage> CameraPreviewPanel => Grid("CameraPreviewPanel");
    public Brinell.Maui.Controls.Container.Grid<MainPage> CameraPreview => Grid("CameraPreview");
    public Brinell.Maui.Controls.Container.Grid<MainPage> CameraActionRail => Grid("CameraActionRail");
    public Brinell.Maui.Controls.Container.Grid<MainPage> CameraActionVariantRail => Grid("CameraActionVariantRail");
    public Button<MainPage> CaptureFrameButton => Button("CaptureFrameButton");

    // Inline camera action rail buttons
    [UatName("Camera Look")]
    public Button<MainPage> CameraLookButton => Button("CameraActionButton_camera_look");

    [UatName("Camera Find")]
    public Button<MainPage> CameraFindButton => Button("CameraActionButton_camera_find");

    [UatName("Camera Read")]
    public Button<MainPage> CameraReadButton => Button("CameraActionButton_camera_read");

    [UatName("Camera Scan")]
    public Button<MainPage> CameraScanButton => Button("CameraActionButton_camera_scan");

    // Camera action variants
    [UatName("Look Overview")]
    public Button<MainPage> LookOverviewButton => Button("CameraActionVariantButton_camera_look_Overview");

    [UatName("Look Summary")]
    public Button<MainPage> LookSummaryButton => Button("CameraActionVariantButton_camera_look_Summary");

    [UatName("Look Detail")]
    public Button<MainPage> LookDetailButton => Button("CameraActionVariantButton_camera_look_Detailed");

    [UatName("Find Overview")]
    public Button<MainPage> FindOverviewButton => Button("CameraActionVariantButton_camera_find_Overview");

    [UatName("Find Summary")]
    public Button<MainPage> FindSummaryButton => Button("CameraActionVariantButton_camera_find_Summary");

    [UatName("Find Detail")]
    public Button<MainPage> FindDetailButton => Button("CameraActionVariantButton_camera_find_Detailed");

    [UatName("Read Summary")]
    public Button<MainPage> ReadSummaryButton => Button("CameraActionVariantButton_camera_read_Summary");

    [UatName("Read Overview")]
    public Button<MainPage> ReadOverviewButton => Button("CameraActionVariantButton_camera_read_Overview");

    [UatName("Read Full")]
    public Button<MainPage> ReadFullButton => Button("CameraActionVariantButton_camera_read_Full");

    [UatName("Scan Default")]
    public Button<MainPage> ScanDefaultButton => Button("CameraActionVariantButton_camera_scan_Default");

    // Debug overlay
    public Label<MainPage> DebugLabel => Label("DebugLabel");
    public Label<MainPage> AudioPolicyDebugLabel => Label("AudioPolicyDebugLabel");

    // Snapshot overlay
    public Label<MainPage> SnapshotCaption => Label("SnapshotCaption");
    public Button<MainPage> DismissSnapshotButton => Button("DismissSnapshotButton");

    public void EnsureActionsExpanded()
    {
        if (ActionsDrawer.WaitVisible(true, 1000) && LookButton.WaitVisible(true, 500))
            return;

        if (!ActionsDrawerButton.WaitVisible(true, 1000))
            ActionsDrawerButton.WaitExists(true, 5000);

        if (ActionsDrawerButton.WaitVisible(true, 1000))
            ActionsDrawerButton.Click();

        ActionsDrawer.WaitVisible(true, 5000);
        LookButton.WaitVisible(true, 5000);
    }
}
