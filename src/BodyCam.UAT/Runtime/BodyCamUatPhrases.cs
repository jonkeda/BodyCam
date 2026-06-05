namespace BodyCam.UAT.Runtime;

[TestScenarioService]
public sealed class BodyCamCameraActionFlow : TestScenarioServiceBase
{
    private const int ShortWaitMs = 750;
    private const int DefaultWaitMs = 5000;
    private const int CameraCommandWaitMs = 15000;

    private readonly BodyCamUatFixture _fixture;
    private readonly MainPage _mainPage;

    public BodyCamCameraActionFlow(
        BodyCamUatFixture fixture,
        MainPage mainPage)
    {
        _fixture = fixture;
        _mainPage = mainPage;
    }

    public void OpenCameraActionSurface()
    {
        _fixture.NavigateToHome();
        _mainPage.EnsureActionsExpanded();
        UatControlAssertions.AssertVisible(_mainPage.ActionsDrawer, "Actions Drawer", DefaultWaitMs);
    }

    public void AssertCameraActionTopLevelButtonsVisible()
    {
        UatControlAssertions.AssertVisible(_mainPage.CameraActionRail, "Camera Action Rail", DefaultWaitMs);
        UatControlAssertions.AssertVisible(_mainPage.CameraLookButton, "Camera Look", DefaultWaitMs);
        UatControlAssertions.AssertVisible(_mainPage.CameraFindButton, "Camera Find", DefaultWaitMs);
        UatControlAssertions.AssertVisible(_mainPage.CameraReadButton, "Camera Read", DefaultWaitMs);
        UatControlAssertions.AssertVisible(_mainPage.CameraScanButton, "Camera Scan", DefaultWaitMs);
    }

    public void AssertCameraActionTopLevelButtonsNotVisible()
    {
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.CameraActionRail, "Camera Action Rail", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.CameraLookButton, "Camera Look", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.CameraFindButton, "Camera Find", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.CameraReadButton, "Camera Read", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.CameraScanButton, "Camera Scan", DefaultWaitMs);
    }

    public void AssertCameraActionVariantsNotVisible()
    {
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.CameraActionVariantRail, "Camera Action Variant Rail", DefaultWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.LookOverviewButton, "Look Overview", ShortWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.FindOverviewButton, "Find Overview", ShortWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.ReadSummaryButton, "Read Summary", ShortWaitMs);
        UatControlAssertions.AssertAbsentOrHidden(_mainPage.ScanDefaultButton, "Scan Default", ShortWaitMs);
    }

    public void WaitForCameraCommandToSettle()
    {
        if (!UatControlAssertions.WaitUntil(
                () => UatControlAssertions.IsAbsentOrHidden(_mainPage.CameraPreviewPanel) &&
                      UatControlAssertions.IsAbsentOrHidden(_mainPage.CameraActionRail) &&
                      UatControlAssertions.IsAbsentOrHidden(_mainPage.CameraActionVariantRail),
                CameraCommandWaitMs))
        {
            throw new InvalidOperationException(
                "Camera command did not close the preview and action rails within the expected time.");
        }
    }

    public void AssertCapturedStillAppearsInTranscript()
    {
        UatControlAssertions.AssertVisible(_mainPage.TranscriptYouEntry, "Transcript You Entry", DefaultWaitMs);
        UatControlAssertions.AssertVisible(_mainPage.TranscriptImageCaption, "Transcript Image Caption", DefaultWaitMs);

        var caption = UatControlAssertions.GetText(_mainPage.TranscriptImageCaption, DefaultWaitMs);
        if (caption?.Contains("Captured frame", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException(
                $"Expected a captured-frame caption in the transcript, but saw '{caption ?? "(null)"}'.");
        }
    }

    public void AssertDeterministicCameraResponseAppearsInTranscript()
    {
        if (!UatControlAssertions.WaitUntil(
                () => UatControlAssertions.GetText(_mainPage.TranscriptAiEntry, DefaultWaitMs)
                    ?.Contains("UAT", StringComparison.OrdinalIgnoreCase) == true,
                CameraCommandWaitMs))
        {
            throw new InvalidOperationException("Expected a deterministic UAT camera response in the transcript.");
        }
    }

    public void AssertTranscriptDoesNotContain(string text)
    {
        string?[] visibleTexts =
        [
            UatControlAssertions.GetTextIfPresent(_mainPage.TranscriptYouEntry, DefaultWaitMs),
            UatControlAssertions.GetTextIfPresent(_mainPage.TranscriptAiEntry, DefaultWaitMs),
            UatControlAssertions.GetTextIfPresent(_mainPage.TranscriptImageCaption, DefaultWaitMs),
            UatControlAssertions.GetTextIfPresent(_mainPage.DebugLabel, DefaultWaitMs)
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

[UatPhraseClass]
public sealed class BodyCamUatPhrases : UatPhraseClassBase
{
    private readonly BodyCamUatFixture _fixture;
    private readonly BodyCamCameraActionFlow _cameraActions;

    public BodyCamUatPhrases(
        BodyCamUatFixture fixture,
        BodyCamCameraActionFlow cameraActions)
    {
        _fixture = fixture;
        _cameraActions = cameraActions;
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

        if (!string.Equals(_fixture.LaunchOptions.Mode, "uat", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"BodyCam UAT launch mode must be 'uat'. Actual value: '{_fixture.LaunchOptions.Mode}'.");
        }

        Directory.CreateDirectory(_fixture.LaunchOptions.AssetsDirectory);
        Directory.CreateDirectory(_fixture.LaunchOptions.ReportsDirectory);
        return $"UAT mode active. Assets: {_fixture.LaunchOptions.AssetsDirectory}. Reports: {_fixture.LaunchOptions.ReportsDirectory}.";
    }

    [UatPhrase(UatEffectiveStepKeyword.Given, "app settings are reset")]
    [UatPhrase(UatEffectiveStepKeyword.When, "I reset the BodyCam UAT scenario state")]
    public void ResetBodyCamUatScenarioState()
    {
        _fixture.ResetScenarioState();
    }

    [UatPhrase(UatEffectiveStepKeyword.Given, "the camera action surface is open")]
    public void OpenCameraActionSurface()
    {
        _cameraActions.OpenCameraActionSurface();
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "camera action top-level buttons should be visible")]
    public void AssertCameraActionTopLevelButtonsVisible()
    {
        _cameraActions.AssertCameraActionTopLevelButtonsVisible();
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "camera action top-level buttons should not be visible")]
    public void AssertCameraActionTopLevelButtonsNotVisible()
    {
        _cameraActions.AssertCameraActionTopLevelButtonsNotVisible();
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "camera action variants should not be visible")]
    public void AssertCameraActionVariantsNotVisible()
    {
        _cameraActions.AssertCameraActionVariantsNotVisible();
    }

    [UatPhrase(UatEffectiveStepKeyword.When, "I wait for camera command to settle")]
    public void WaitForCameraCommandToSettle()
    {
        _cameraActions.WaitForCameraCommandToSettle();
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "the captured still should appear in the transcript")]
    public void AssertCapturedStillAppearsInTranscript()
    {
        _cameraActions.AssertCapturedStillAppearsInTranscript();
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "the deterministic camera response should appear in the transcript")]
    public void AssertDeterministicCameraResponseAppearsInTranscript()
    {
        _cameraActions.AssertDeterministicCameraResponseAppearsInTranscript();
    }

    [UatPhrase(UatEffectiveStepKeyword.Then, "transcript should not contain {text}")]
    public void AssertTranscriptDoesNotContain(string text)
    {
        _cameraActions.AssertTranscriptDoesNotContain(text);
    }
}
