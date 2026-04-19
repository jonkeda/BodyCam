using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Input;
using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Tools;

namespace BodyCam.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IApiKeyService _apiKeyService;
    private readonly Func<HttpClient> _httpClientFactory;
    private string? _fullKey;

    private readonly CameraManager _cameraManager;
    private readonly AudioInputManager _audioInputManager;
    private readonly AudioOutputManager _audioOutputManager;

    public SettingsViewModel(ISettingsService settings, IApiKeyService apiKeyService, IEnumerable<ITool> tools, CameraManager cameraManager, AudioInputManager audioInputManager, AudioOutputManager audioOutputManager, Func<HttpClient>? httpClientFactory = null)
    {
        _settings = settings;
        _apiKeyService = apiKeyService;
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        _cameraManager = cameraManager;
        _audioInputManager = audioInputManager;
        _audioOutputManager = audioOutputManager;
        Title = "Settings";

        ChangeApiKeyCommand = new AsyncRelayCommand(ChangeApiKeyAsync);
        ClearApiKeyCommand = new AsyncRelayCommand(ClearApiKeyAsync);
        ToggleKeyVisibilityCommand = new RelayCommand(ToggleKeyVisibility);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);

        // Refresh pickers when BT devices connect/disconnect
        _audioInputManager.ProvidersChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AudioInputProviders));
            OnPropertyChanged(nameof(SelectedAudioInputProvider));
        };

        _audioOutputManager.ProvidersChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AudioOutputProviders));
            OnPropertyChanged(nameof(SelectedAudioOutputProvider));
        };

        LoadApiKeyDisplay();
        LoadToolSettings(tools);
    }

    public ObservableCollection<ToolSettingsSection> ToolSettingsSections { get; } = new();

    private void LoadToolSettings(IEnumerable<ITool> tools)
    {
        foreach (var tool in tools.OfType<IToolSettings>())
        {
            tool.LoadSettings(_settings);
            var section = new ToolSettingsSection
            {
                DisplayName = tool.SettingsDisplayName,
                Description = tool.SettingsDescription
            };

            foreach (var descriptor in tool.GetSettingDescriptors())
            {
                var item = new ToolSettingItem(descriptor);
                item.LoadFromDescriptor();
                section.Items.Add(item);
            }

            ToolSettingsSections.Add(section);
        }
    }

    public void SaveToolSettings()
    {
        // Settings are already applied via SetValue callbacks
    }

    // --- Provider ---

    public OpenAiProvider SelectedProvider
    {
        get => _settings.Provider;
        set
        {
            if (SetProperty(_settings.Provider, value, v => _settings.Provider = v))
            {
                OnPropertyChanged(nameof(IsOpenAi));
                OnPropertyChanged(nameof(IsAzure));
            }
        }
    }

    public bool IsOpenAi => SelectedProvider != OpenAiProvider.Azure;
    public bool IsAzure => SelectedProvider == OpenAiProvider.Azure;

    // --- Model Picker Options ---

    public ModelInfo[] RealtimeModelOptions => ModelOptions.RealtimeModels;
    public ModelInfo[] ChatModelOptions => ModelOptions.ChatModels;
    public ModelInfo[] VisionModelOptions => ModelOptions.VisionModels;
    public ModelInfo[] TranscriptionModelOptions => ModelOptions.TranscriptionModels;
    public string[] VoiceOptions => ModelOptions.Voices;
    public string[] TurnDetectionOptions => ModelOptions.TurnDetectionModes;
    public string[] NoiseReductionOptions => ModelOptions.NoiseReductionModes;

    // --- Model Selections ---

    public ModelInfo? SelectedRealtimeModel
    {
        get => ModelOptions.RealtimeModels.FirstOrDefault(m => m.Id == _settings.RealtimeModel);
        set
        {
            if (value is not null)
                SetProperty(_settings.RealtimeModel, value.Id, v => _settings.RealtimeModel = v);
        }
    }

    public ModelInfo? SelectedChatModel
    {
        get => ModelOptions.ChatModels.FirstOrDefault(m => m.Id == _settings.ChatModel);
        set
        {
            if (value is not null)
                SetProperty(_settings.ChatModel, value.Id, v => _settings.ChatModel = v);
        }
    }

    public ModelInfo? SelectedVisionModel
    {
        get => ModelOptions.VisionModels.FirstOrDefault(m => m.Id == _settings.VisionModel);
        set
        {
            if (value is not null)
                SetProperty(_settings.VisionModel, value.Id, v => _settings.VisionModel = v);
        }
    }

    public ModelInfo? SelectedTranscriptionModel
    {
        get => ModelOptions.TranscriptionModels.FirstOrDefault(m => m.Id == _settings.TranscriptionModel);
        set
        {
            if (value is not null)
                SetProperty(_settings.TranscriptionModel, value.Id, v => _settings.TranscriptionModel = v);
        }
    }

    // --- Azure Deployment Names ---

    public string AzureEndpoint
    {
        get => _settings.AzureEndpoint ?? string.Empty;
        set => SetProperty(_settings.AzureEndpoint ?? string.Empty, value,
            v => _settings.AzureEndpoint = string.IsNullOrWhiteSpace(v) ? null : v);
    }

    public string AzureApiVersion
    {
        get => _settings.AzureApiVersion;
        set => SetProperty(_settings.AzureApiVersion, value, v => _settings.AzureApiVersion = v);
    }

    public string AzureRealtimeDeployment
    {
        get => _settings.AzureRealtimeDeploymentName ?? string.Empty;
        set => SetProperty(_settings.AzureRealtimeDeploymentName ?? string.Empty, value,
            v => _settings.AzureRealtimeDeploymentName = string.IsNullOrWhiteSpace(v) ? null : v);
    }

    public string AzureChatDeployment
    {
        get => _settings.AzureChatDeploymentName ?? string.Empty;
        set => SetProperty(_settings.AzureChatDeploymentName ?? string.Empty, value,
            v => _settings.AzureChatDeploymentName = string.IsNullOrWhiteSpace(v) ? null : v);
    }

    public string AzureVisionDeployment
    {
        get => _settings.AzureVisionDeploymentName ?? string.Empty;
        set => SetProperty(_settings.AzureVisionDeploymentName ?? string.Empty, value,
            v => _settings.AzureVisionDeploymentName = string.IsNullOrWhiteSpace(v) ? null : v);
    }

    // --- Voice Settings ---

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

    // --- System Instructions ---

    public string SystemInstructions
    {
        get => _settings.SystemInstructions;
        set => SetProperty(_settings.SystemInstructions, value, v => _settings.SystemInstructions = v);
    }

    // --- Debug ---

    public bool DebugMode
    {
        get => _settings.DebugMode;
        set => SetProperty(_settings.DebugMode, value, v => _settings.DebugMode = v);
    }

    public bool ShowTokenCounts
    {
        get => _settings.ShowTokenCounts;
        set => SetProperty(_settings.ShowTokenCounts, value, v => _settings.ShowTokenCounts = v);
    }

    public bool ShowCostEstimate
    {
        get => _settings.ShowCostEstimate;
        set => SetProperty(_settings.ShowCostEstimate, value, v => _settings.ShowCostEstimate = v);
    }

    // --- Diagnostics & Telemetry ---

    public bool SendDiagnosticData
    {
        get => _settings.SendDiagnosticData;
        set => SetProperty(_settings.SendDiagnosticData, value, v => _settings.SendDiagnosticData = v);
    }

    public string? AzureMonitorConnectionString
    {
        get => _settings.AzureMonitorConnectionString;
        set => SetProperty(_settings.AzureMonitorConnectionString, value, v => _settings.AzureMonitorConnectionString = v);
    }

    public bool SendCrashReports
    {
        get => _settings.SendCrashReports;
        set => SetProperty(_settings.SendCrashReports, value, v => _settings.SendCrashReports = v);
    }

    public string? SentryDsn
    {
        get => _settings.SentryDsn;
        set => SetProperty(_settings.SentryDsn, value, v => _settings.SentryDsn = v);
    }

    public bool SendUsageData
    {
        get => _settings.SendUsageData;
        set => SetProperty(_settings.SendUsageData, value, v => _settings.SendUsageData = v);
    }

    // --- API Key ---

    private string _apiKeyDisplay = string.Empty;
    public string ApiKeyDisplay
    {
        get => _apiKeyDisplay;
        set => SetProperty(ref _apiKeyDisplay, value);
    }

    private bool _isKeyVisible;
    public bool IsKeyVisible
    {
        get => _isKeyVisible;
        set
        {
            SetProperty(ref _isKeyVisible, value);
            OnPropertyChanged(nameof(KeyToggleText));
            ApiKeyDisplay = value ? (_fullKey ?? "(not set)") : MaskKey(_fullKey);
        }
    }

    public string KeyToggleText => IsKeyVisible ? "Hide" : "Show";

    public ICommand ChangeApiKeyCommand { get; }
    public ICommand ClearApiKeyCommand { get; }
    public ICommand ToggleKeyVisibilityCommand { get; }

    private void ToggleKeyVisibility() => IsKeyVisible = !IsKeyVisible;

    private async void LoadApiKeyDisplay()
    {
        _fullKey = await _apiKeyService.GetApiKeyAsync();
        ApiKeyDisplay = MaskKey(_fullKey);
    }

    private async Task ChangeApiKeyAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;

        var key = await page.DisplayPromptAsync(
            "API Key",
            "Enter your OpenAI or Azure OpenAI API key:",
            placeholder: "sk-proj-... or Azure key",
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (!string.IsNullOrWhiteSpace(key))
        {
            await _apiKeyService.SetApiKeyAsync(key);
            _fullKey = key;
            IsKeyVisible = false;
            ApiKeyDisplay = MaskKey(key);
        }
    }

    private async Task ClearApiKeyAsync()
    {
        await _apiKeyService.ClearApiKeyAsync();
        _fullKey = null;
        IsKeyVisible = false;
        ApiKeyDisplay = "(not set)";
    }

    private static string MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "(not set)";
        if (key.Length <= 8) return "****";
        return key[..4] + "****" + key[^4..];
    }

    // --- Camera ---

    public IReadOnlyList<ICameraProvider> CameraProviders => _cameraManager.Providers;

    public ICameraProvider? SelectedCameraProvider
    {
        get => _cameraManager.Active;
        set
        {
            if (value is not null && value != _cameraManager.Active)
            {
                _ = _cameraManager.SetActiveAsync(value.ProviderId);
                OnPropertyChanged();
            }
        }
    }

    // --- Audio Input ---

    public IReadOnlyList<IAudioInputProvider> AudioInputProviders => _audioInputManager.Providers;

    public IAudioInputProvider? SelectedAudioInputProvider
    {
        get => _audioInputManager.Active;
        set
        {
            if (value is not null && value != _audioInputManager.Active)
            {
                _ = _audioInputManager.SetActiveAsync(value.ProviderId);
                OnPropertyChanged();
            }
        }
    }

    // --- Audio Output ---

    public IReadOnlyList<IAudioOutputProvider> AudioOutputProviders => _audioOutputManager.Providers;

    public IAudioOutputProvider? SelectedAudioOutputProvider
    {
        get => _audioOutputManager.Active;
        set
        {
            if (value is not null && value != _audioOutputManager.Active)
            {
                _ = _audioOutputManager.SetActiveAsync(value.ProviderId);
                OnPropertyChanged();
            }
        }
    }

    // --- Test Connection ---

    private string _connectionStatus = string.Empty;
    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }

    private string _realtimeStatus = string.Empty;
    public string RealtimeStatus
    {
        get => _realtimeStatus;
        set => SetProperty(ref _realtimeStatus, value);
    }

    private string _chatStatus = string.Empty;
    public string ChatStatus
    {
        get => _chatStatus;
        set => SetProperty(ref _chatStatus, value);
    }

    private string _visionStatus = string.Empty;
    public string VisionStatus
    {
        get => _visionStatus;
        set => SetProperty(ref _visionStatus, value);
    }

    public ICommand TestConnectionCommand { get; }

    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        ConnectionStatus = "Testing...";
        RealtimeStatus = ChatStatus = VisionStatus = string.Empty;

        try
        {
            var key = await _apiKeyService.GetApiKeyAsync();
            if (string.IsNullOrEmpty(key))
            {
                ConnectionStatus = "✗ No API key configured";
                return;
            }

            using var http = _httpClientFactory();

            if (IsAzure)
            {
                var endpoint = _settings.AzureEndpoint;
                var version = _settings.AzureApiVersion;
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    ConnectionStatus = "✗ Azure endpoint is empty";
                    return;
                }

                http.DefaultRequestHeaders.Add("api-key", key);
                var baseUrl = $"{endpoint.TrimEnd('/')}/openai/deployments";

                var tasks = new[]
                {
                    ProbeAzureDeployment(http, baseUrl, _settings.AzureRealtimeDeploymentName, version, s => RealtimeStatus = s),
                    ProbeAzureDeployment(http, baseUrl, _settings.AzureChatDeploymentName, version, s => ChatStatus = s),
                    ProbeAzureDeployment(http, baseUrl, _settings.AzureVisionDeploymentName, version, s => VisionStatus = s),
                };
                await Task.WhenAll(tasks);
            }
            else
            {
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
                var resp = await http.GetAsync("https://api.openai.com/v1/models");
                if (!resp.IsSuccessStatusCode)
                {
                    ConnectionStatus = $"✗ OpenAI returned {(int)resp.StatusCode}: {resp.ReasonPhrase}";
                    return;
                }

                var modelIds = await ParseModelIds(resp);

                RealtimeStatus = modelIds.Contains(_settings.RealtimeModel) ? "✓" : "✗ not found";
                ChatStatus = modelIds.Contains(_settings.ChatModel) ? "✓" : "✗ not found";
                VisionStatus = modelIds.Contains(_settings.VisionModel) ? "✓" : "✗ not found";
            }

            var statuses = new[] { RealtimeStatus, ChatStatus, VisionStatus };
            var passed = statuses.Count(s => s == "✓");
            var skipped = statuses.Count(s => s == "—");
            var total = statuses.Length - skipped;

            ConnectionStatus = passed == total
                ? "✓ All models verified"
                : passed == 0 ? "✗ All models failed" : $"⚠ {passed}/{total} models verified";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"✗ {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private const string ProbeJson = """{"messages":[{"role":"user","content":"test"}],"max_completion_tokens":1}""";

    private static async Task ProbeAzureDeployment(HttpClient http, string baseUrl, string? deployment, string version, Action<string> setStatus)
    {
        if (string.IsNullOrWhiteSpace(deployment))
        {
            setStatus("—");
            return;
        }

        try
        {
            // POST a minimal chat completion — any non-404 proves the deployment exists.
            // 404 = DeploymentNotFound; 400 (max_tokens) or 200 = deployment exists.
            var uri = $"{baseUrl}/{deployment}/chat/completions?api-version={version}";
            using var content = new StringContent(ProbeJson, System.Text.Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(uri, content);
            setStatus(resp.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "✗ not found"
                : "✓");
        }
        catch (Exception ex)
        {
            setStatus($"✗ {ex.Message}");
        }
    }

    private static async Task<HashSet<string>> ParseModelIds(HttpResponseMessage resp)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        ids.Add(id.GetString()!);
                }
            }
        }
        catch { }
        return ids;
    }
}
