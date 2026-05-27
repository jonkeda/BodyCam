namespace BodyCam.UITests.Pages;

public class AddDevicesPage : PageObjectBase<AddDevicesPage>
{
    public AddDevicesPage(IMauiTestContext context) : base(context) { }

    public override string Name => "AddDevicesPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => AddCyanGlassesButton.IsExists();

    public Brinell.Maui.Controls.Collection.CollectionView<AddDevicesPage> AddDevicesList
        => CollectionView("AddDevicesList");

    public Button<AddDevicesPage> AddCyanGlassesButton => Button("AddCyanGlassesButton");
}
