namespace BodyCam.UAT.Runtime;

[TestModuleScan(typeof(BodyCamUatFixture), NamespacePrefix = "BodyCam.UAT.Runtime")]
[TestModuleScan(typeof(MainPage), NamespacePrefix = "BodyCam.UITestKit.Pages")]
public sealed class BodyCamUatFixture : BodyCamFixture
{
    public BodyCamUatFixture()
        : base(BodyCamUatEnvironment.Apply())
    {
        Composition = TestComposition.ForFixture(this, services =>
            services.AddSingleton<IMauiTestContext>(Context));

        ResetScenarioState();
    }

    public TestComposition Composition { get; }

    public void NavigateToMain()
    {
        NavigateToHome();
    }
}
