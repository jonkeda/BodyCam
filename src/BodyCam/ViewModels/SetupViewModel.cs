using System.Collections.ObjectModel;
using System.Windows.Input;
using BodyCam.Models;
using BodyCam.Mvvm;
using BodyCam.Services;

namespace BodyCam.ViewModels;

public class SetupViewModel : ViewModelBase
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ISettingsService _settings;
    private readonly AppSettings _appSettings;

    public SetupViewModel(IApiKeyService apiKeyService, ISettingsService settings, AppSettings appSettings)
    {
        _apiKeyService = apiKeyService;
        _settings = settings;
        _appSettings = appSettings;

        RequestPermissionCommand = new AsyncRelayCommand(RequestPermissionAsync);
        NextCommand = new AsyncRelayCommand(NextAsync);
        SkipCommand = new AsyncRelayCommand(SkipAsync);
        ValidateKeyCommand = new AsyncRelayCommand(ValidateKeyAsync);
        OpenSettingsCommand = new RelayCommand(OpenAppSettings);

        InitializeSteps();
    }

    // Steps collection
    public ObservableCollection<SetupStep> Steps { get; } = [];

    // Current step tracking
    private int _currentStep;
    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (SetProperty(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(CurrentTitle));
                OnPropertyChanged(nameof(CurrentDescription));
                OnPropertyChanged(nameof(CurrentIcon));
                OnPropertyChanged(nameof(CurrentStepStatus));
                OnPropertyChanged(nameof(IsPermissionStep));
                OnPropertyChanged(nameof(IsApiKeyStep));
                OnPropertyChanged(nameof(IsLastStep));
                OnPropertyChanged(nameof(NextButtonText));
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(ShowDeniedHelp));
            }
        }
    }

    // Computed properties from current step
    public string CurrentTitle => CurrentStep < Steps.Count ? Steps[CurrentStep].Title : string.Empty;
    public string CurrentDescription => CurrentStep < Steps.Count ? Steps[CurrentStep].Description : string.Empty;
    public string CurrentIcon => CurrentStep < Steps.Count ? Steps[CurrentStep].Icon : string.Empty;
    public string CurrentStepStatus => CurrentStep < Steps.Count ? Steps[CurrentStep].Status : "pending";
    public bool IsPermissionStep => CurrentStep < Steps.Count && Steps[CurrentStep].Kind == SetupStepKind.Permission;
    public bool IsApiKeyStep => CurrentStep < Steps.Count && Steps[CurrentStep].Kind == SetupStepKind.ApiKey;
    public bool IsLastStep => CurrentStep >= Steps.Count - 1;
    public string NextButtonText => IsLastStep ? "Get Started" : "Next";
    public string ProgressText => $"Step {CurrentStep + 1} of {Steps.Count}";
    public bool ShowDeniedHelp => CurrentStepStatus == "denied";

    // API Key entry fields
    private string _apiKey = string.Empty;
    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    private bool _isOpenAi = true;
    public bool IsOpenAi
    {
        get => _isOpenAi;
        set
        {
            if (SetProperty(ref _isOpenAi, value) && value)
                IsAzure = false;
        }
    }

    private bool _isAzure;
    public bool IsAzure
    {
        get => _isAzure;
        set
        {
            if (SetProperty(ref _isAzure, value) && value)
                IsOpenAi = false;
        }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _isValidating;
    public bool IsValidating
    {
        get => _isValidating;
        set => SetProperty(ref _isValidating, value);
    }

    // Commands
    public ICommand RequestPermissionCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand ValidateKeyCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    // Event raised when setup is complete — page navigates away
    public event EventHandler? SetupFinished;

    private void InitializeSteps()
    {
        // On Windows, only show API key step. On Android, show permission steps first.
#if ANDROID
        Steps.Add(new SetupStep
        {
            Title = "Microphone",
            Description = "BodyCam needs your microphone for voice conversations with the AI assistant.",
            Icon = "🎤",
            Kind = SetupStepKind.Permission
        });
        Steps.Add(new SetupStep
        {
            Title = "Camera",
            Description = "BodyCam uses your camera to see and describe what's around you.",
            Icon = "📷",
            Kind = SetupStepKind.Permission
        });
        Steps.Add(new SetupStep
        {
            Title = "Bluetooth",
            Description = "BodyCam can connect to Bluetooth audio devices like smart glasses.",
            Icon = "🎧",
            Kind = SetupStepKind.Permission
        });
#endif
        Steps.Add(new SetupStep
        {
            Title = "API Key",
            Description = "Enter your OpenAI or Azure OpenAI API key to enable AI features. You can also set this later in Settings.",
            Icon = "🔑",
            Kind = SetupStepKind.ApiKey
        });

        // Pre-check permissions that are already granted
        _ = CheckExistingPermissionsAsync();
        // Pre-fill provider from settings
        IsOpenAi = _settings.Provider == OpenAiProvider.OpenAi;
        IsAzure = _settings.Provider == OpenAiProvider.Azure;
    }

    private async Task CheckExistingPermissionsAsync()
    {
#if ANDROID
        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (step.Kind != SetupStepKind.Permission) continue;

            var status = step.Title switch
            {
                "Microphone" => await Permissions.CheckStatusAsync<Permissions.Microphone>(),
                "Camera" => await Permissions.CheckStatusAsync<Permissions.Camera>(),
                "Bluetooth" => await Permissions.CheckStatusAsync<Permissions.Bluetooth>(),
                _ => PermissionStatus.Unknown
            };

            if (status == PermissionStatus.Granted)
                step.Status = "granted";
        }

        // If current step is already granted, skip ahead
        SkipGrantedSteps();
#endif
        // Check if API key already exists (must call GetApiKeyAsync to discover .env / SecureStorage)
        var existingKey = await _apiKeyService.GetApiKeyAsync();
        if (existingKey is not null)
        {
            var keyStep = Steps.FirstOrDefault(s => s.Kind == SetupStepKind.ApiKey);
            if (keyStep is not null)
                keyStep.Status = "granted";
        }

        // Skip past any steps that are already satisfied (permissions + API key)
        SkipGrantedSteps();

        // If everything is granted, finish setup automatically
        if (Steps.All(s => s.Status == "granted"))
            await FinishSetupAsync();
    }

    private void SkipGrantedSteps()
    {
        while (CurrentStep < Steps.Count && Steps[CurrentStep].Status == "granted")
            CurrentStep++;

        if (CurrentStep >= Steps.Count)
            CurrentStep = Steps.Count - 1;
    }

    private async Task RequestPermissionAsync()
    {
#if ANDROID
        if (CurrentStep >= Steps.Count) return;
        var step = Steps[CurrentStep];
        if (step.Kind != SetupStepKind.Permission) return;

        var status = step.Title switch
        {
            "Microphone" => await Permissions.RequestAsync<Permissions.Microphone>(),
            "Camera" => await Permissions.RequestAsync<Permissions.Camera>(),
            "Bluetooth" => await Permissions.RequestAsync<Permissions.Bluetooth>(),
            _ => PermissionStatus.Unknown
        };

        step.Status = status == PermissionStatus.Granted ? "granted" : "denied";
        OnPropertyChanged(nameof(CurrentStepStatus));
        OnPropertyChanged(nameof(ShowDeniedHelp));
#endif
    }

    private async Task NextAsync()
    {
        if (IsLastStep)
        {
            await FinishSetupAsync();
            return;
        }

        CurrentStep++;
        SkipGrantedSteps();
    }

    private async Task SkipAsync()
    {
        if (CurrentStep < Steps.Count)
            Steps[CurrentStep].Status = "skipped";

        await NextAsync();
    }

    private async Task ValidateKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "Please enter an API key.";
            return;
        }

        IsValidating = true;
        StatusMessage = "Validating...";

        try
        {
            // Save the provider setting
            _settings.Provider = IsAzure ? OpenAiProvider.Azure : OpenAiProvider.OpenAi;
            _appSettings.Provider = _settings.Provider;

            // Save the key
            await _apiKeyService.SetApiKeyAsync(ApiKey.Trim());

            // Try a lightweight API call to validate
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            if (IsAzure && !string.IsNullOrEmpty(_settings.AzureEndpoint))
            {
                http.DefaultRequestHeaders.Add("api-key", ApiKey.Trim());
                var response = await http.GetAsync($"{_settings.AzureEndpoint.TrimEnd('/')}/openai/models?api-version={_settings.AzureApiVersion}");
                if (response.IsSuccessStatusCode)
                {
                    StatusMessage = "✓ Connected to Azure OpenAI";
                    Steps[CurrentStep].Status = "granted";
                    OnPropertyChanged(nameof(CurrentStepStatus));
                }
                else
                {
                    StatusMessage = $"✕ Azure returned {(int)response.StatusCode}. Check your key and endpoint.";
                }
            }
            else
            {
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey.Trim()}");
                var response = await http.GetAsync("https://api.openai.com/v1/models");
                if (response.IsSuccessStatusCode)
                {
                    StatusMessage = "✓ Connected to OpenAI";
                    Steps[CurrentStep].Status = "granted";
                    OnPropertyChanged(nameof(CurrentStepStatus));
                }
                else
                {
                    StatusMessage = $"✕ OpenAI returned {(int)response.StatusCode}. Check your API key.";
                }
            }
        }
        catch (TaskCanceledException)
        {
            StatusMessage = "✕ Request timed out. Check your internet connection.";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"✕ Network error: {ex.Message}";
        }
        finally
        {
            IsValidating = false;
        }
    }

    private void OpenAppSettings()
    {
        AppInfo.ShowSettingsUI();
    }

    private async Task FinishSetupAsync()
    {
        _settings.SetupCompleted = true;
        SetupFinished?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }
}
