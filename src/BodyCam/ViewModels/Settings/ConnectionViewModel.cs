using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Input;
using BodyCam.Mvvm;
using BodyCam.Services;

namespace BodyCam.ViewModels.Settings;

public class ConnectionViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IApiKeyService _apiKeyService;
    private readonly Func<HttpClient> _httpClientFactory;
    private string? _fullKey;

    public ConnectionViewModel(ISettingsService settings, IApiKeyService apiKeyService, Func<HttpClient>? httpClientFactory = null)
    {
        _settings = settings;
        _apiKeyService = apiKeyService;
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        Title = "Connection";

        ChangeApiKeyCommand = new AsyncRelayCommand(ChangeApiKeyAsync);
        ClearApiKeyCommand = new AsyncRelayCommand(ClearApiKeyAsync);
        ToggleKeyVisibilityCommand = new RelayCommand(ToggleKeyVisibility);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);

        LoadApiKeyDisplay();
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
