using BodyCam.Mvvm;
using BodyCam.Services;

namespace BodyCam.ViewModels.Settings;

public class VoiceViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;

    public VoiceViewModel(ISettingsService settings)
    {
        _settings = settings;
        Title = "Voice & AI";
    }

    public string[] VoiceOptions => ModelOptions.Voices;
    public string[] TurnDetectionOptions => ModelOptions.TurnDetectionModes;
    public string[] NoiseReductionOptions => ModelOptions.NoiseReductionModes;

    public string SelectedVoice
    {
        get => _settings.Voice;
        set => SetProperty(_settings.Voice, value, v => _settings.Voice = v);
    }

    public string SelectedTurnDetection
    {
        get => _settings.TurnDetection;
        set => SetProperty(_settings.TurnDetection, value, v => _settings.TurnDetection = v);
    }

    public string SelectedNoiseReduction
    {
        get => _settings.NoiseReduction;
        set => SetProperty(_settings.NoiseReduction, value, v => _settings.NoiseReduction = v);
    }

    public string SystemInstructions
    {
        get => _settings.SystemInstructions;
        set => SetProperty(_settings.SystemInstructions, value, v => _settings.SystemInstructions = v);
    }
}
