namespace BodyCam.UAT.Runtime;

public sealed class BodyCamUatFixture : BodyCamFixture
{
    private const int ShortWaitMs = 750;
    private const int DefaultWaitMs = 5000;
    private const int CameraCommandWaitMs = 15000;

    public BodyCamUatFixture()
        : base(BodyCamUatEnvironment.Apply())
    {
        ResetScenarioState();
    }

    [UatPhrase(UatEffectiveStepKeyword.Given, "the app is running in deterministic UAT mode")]
    [UatPhrase(UatEffectiveStepKeyword.Then, "BodyCam UAT environment should be deterministic")]
    public string AssertDeterministicUatMode(BodyCamTestSettings settings)
    {
        if (!string.Equals(settings.Uat.StartupMode, "deterministic", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"BodyCam UAT settings must use startup mode 'deterministic'. Actual value: '{settings.Uat.StartupMode}'.");
        }

        var mode = Environment.GetEnvironmentVariable(BodyCamUatEnvironment.TestModeVariable);
        if (!string.Equals(mode, "uat", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{BodyCamUatEnvironment.TestModeVariable} must be 'uat'. Actual value: '{mode ?? "(null)"}'.");
        }

        Directory.CreateDirectory(LaunchOptions.AssetsDirectory);
        Directory.CreateDirectory(LaunchOptions.ReportsDirectory);
        return $"UAT mode active. Assets: {LaunchOptions.AssetsDirectory}. Reports: {LaunchOptions.ReportsDirectory}.";
    }

    [UatPhrase(UatEffectiveStepKeyword.Given, "app settings are reset")]
    [UatPhrase(UatEffectiveStepKeyword.When, "I reset the BodyCam UAT scenario state")]
    public void ResetBodyCamUatScenarioState()
    {
        ResetScenarioState();
    }

    [UatPhrase(UatEffectiveStepKeyword.Given, "the camera action surface is open")]
    public void OpenCameraActionSurface()
    {
        NavigateToHome();
        MainPage.EnsureActionsExpanded();
        UatControlAssertions.AssertVisible(MainPage.ActionsDrawer, "Actions Drawer", DefaultWaitMs);
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "camera action top-level buttons should be visible")]
    public void AssertCameraActionTopLevelButtonsVisible()
    {
        UatControlAssertions.AssertVisible(MainPage.CameraActionRail, "Camera Action Rail", DefaultWaitMs);
        UatControlAssertions.AssertVisible(MainPage.CameraLookButton, "Camera Look", DefaultWaitMs);
        UatControlAssertions.AssertVisible(MainPage.CameraFindButton, "Camera Find", DefaultWaitMs);
        UatControlAssertions.AssertVisible(MainPage.CameraReadButton, "Camera Read", DefaultWaitMs);
        UatControlAssertions.AssertVisible(MainPage.CameraScanButton, "Camera Scan", DefaultWaitMs);
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "camera action top-level buttons should not be visible")]
    public void AssertCameraActionTopLevelButtonsNotVisible()
    {
        UatControlAssertions.AssertAbsentOrHidden(MainPage.CameraActionRail, "Camera Action Rail", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(MainPage.CameraLookButton, "Camera Look", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(MainPage.CameraFindButton, "Camera Find", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(MainPage.CameraReadButton, "Camera Read", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(MainPage.CameraScanButton, "Camera Scan", DefaultWaitMs);
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "camera action variants should not be visible")]
    public void AssertCameraActionVariantsNotVisible()
    {
        UatControlAssertions.AssertAbsentOrHidden(MainPage.CameraActionVariantRail, "Camera Action Variant Rail", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(MainPage.LookOverviewButton, "Look Overview", ShortWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(MainPage.FindOverviewButton, "Find Overview", ShortWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(MainPage.ReadSummaryButton, "Read Summary", ShortWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(MainPage.ScanDefaultButton, "Scan Default", ShortWaitMs);
    }

    [UatPhrase(UatEffectiveStepKeyword.When, "I wait for camera command to settle")]
    public void WaitForCameraCommandToSettle()
    {
        if (!UatControlAssertions.WaitUntil(
                () => UatControlAssertions.IsAbsentOrHidden(MainPage.CameraPreviewPanel) &&
                      UatControlAssertions.IsAbsentOrHidden(MainPage.CameraActionRail) &&
                      UatControlAssertions.IsAbsentOrHidden(MainPage.CameraActionVariantRail),
                CameraCommandWaitMs))
        {
            throw new InvalidOperationException(
                "Camera command did not close the preview and action rails within the expected time.");
        }
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "the captured still should appear in the transcript")]
    public void AssertCapturedStillAppearsInTranscript()
    {
        UatControlAssertions.AssertVisible(MainPage.TranscriptYouEntry, "Transcript You Entry", DefaultWaitMs);
        UatControlAssertions.AssertVisible(MainPage.TranscriptImageCaption, "Transcript Image Caption", DefaultWaitMs);

        var caption = UatControlAssertions.GetText(MainPage.TranscriptImageCaption, DefaultWaitMs);
        if (caption?.Contains("Captured frame", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException(
                $"Expected a captured-frame caption in the transcript, but saw '{caption ?? "(null)"}'.");
        }
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "the deterministic camera response should appear in the transcript")]
    public void AssertDeterministicCameraResponseAppearsInTranscript()
    {
        if (!UatControlAssertions.WaitUntil(
                () => UatControlAssertions.GetText(MainPage.TranscriptAiEntry, DefaultWaitMs)
                    ?.Contains("UAT", StringComparison.OrdinalIgnoreCase) == true,
                CameraCommandWaitMs))
        {
            throw new InvalidOperationException("Expected a deterministic UAT camera response in the transcript.");
        }
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "transcript should not contain {text}")]
    public void AssertTranscriptDoesNotContain(string text)
    {
        string?[] visibleTexts =
        [
            UatControlAssertions.GetTextIfPresent(MainPage.TranscriptYouEntry, DefaultWaitMs),
            UatControlAssertions.GetTextIfPresent(MainPage.TranscriptAiEntry, DefaultWaitMs),
            UatControlAssertions.GetTextIfPresent(MainPage.TranscriptImageCaption, DefaultWaitMs),
            UatControlAssertions.GetTextIfPresent(MainPage.DebugLabel, DefaultWaitMs)
        ];

        var hit = visibleTexts.FirstOrDefault(value =>
            value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
        if (hit is not null)
        {
            throw new InvalidOperationException(
                $"Expected transcript/debug text not to contain '{text}', but saw '{hit}'.");
        }
    }
}
