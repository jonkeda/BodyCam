using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Input;
using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.AiProviders;

namespace BodyCam.ViewModels.Settings;

public class ConnectionViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IApiKeyService _apiKeyService;
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly Func<HttpClient> _httpClientFactory;
    private string? _fullKey;

    public ConnectionViewModel(
        ISettingsService settings,
        IApiKeyService apiKeyService,
        IAiProviderRegistry? providerRegistry = null,
        Func<HttpClient>? httpClientFactory = null)
    {
        _settings = settings;
        _apiKeyService = apiKeyService;
        _providerRegistry = providerRegistry ?? AiProviderRegistry.Default;
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        Title = "Connection";
        ProviderOptions = _providerRegistry.Providers
            .Select(provider => new ProviderOptionViewModel(provider))
            .ToArray();

        ChangeApiKeyCommand = new AsyncRelayCommand(ChangeApiKeyAsync);
        ClearApiKeyCommand = new AsyncRelayCommand(ClearApiKeyAsync);
        ToggleKeyVisibilityCommand = new RelayCommand(ToggleKeyVisibility);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);

        RefreshProviderSelection();
        LoadApiKeyDisplay();
    }

    // --- Provider ---

    public IReadOnlyList<ProviderOptionViewModel> ProviderOptions { get; }

    public string SelectedProviderId
    {
        get => AiProviderIds.Normalize(_settings.ProviderId);
        set
        {
            var providerId = AiProviderIds.Normalize(value);
            var provider = _providerRegistry.TryGet(providerId);
            if (provider is null || !provider.IsSelectable)
                return;

            if (SetProperty(SelectedProviderId, providerId, v => _settings.ProviderId = v))
                NotifyProviderChanged();
        }
    }

    public AiProviderDefinition SelectedProviderDefinition =>
        _providerRegistry.TryGet(SelectedProviderId)
        ?? _providerRegistry.GetRequired(AiProviderIds.OpenAi);

    [Obsolete("Use SelectedProviderId instead.")]
    public OpenAiProvider SelectedProvider
    {
        get => AiProviderIds.ToLegacyProvider(SelectedProviderId);
        set => SelectedProviderId = AiProviderIds.FromLegacyProvider(value);
    }

    public bool IsOpenAi => SelectedProviderId == AiProviderIds.OpenAi;
    public bool IsAzure => SelectedProviderId == AiProviderIds.AzureOpenAi;
    public bool IsGrok => SelectedProviderId == AiProviderIds.XaiGrok;

    public string ApiKeySectionTitle => IsGrok ? "xAI API Key" : "API Key";

    public string ApiKeyHelpText
    {
        get
        {
            var provider = SelectedProviderDefinition;
            if (provider.Id == AiProviderIds.XaiGrok)
            {
                return provider.CredentialPolicy.OAuthUnavailableReason
                    ?? "Grok currently uses xAI API-key auth.";
            }

            return "Stored securely on this device.";
        }
    }

    public bool ShowCredentialNotice => !string.IsNullOrWhiteSpace(ApiKeyHelpText);

    private void NotifyProviderChanged()
    {
        RefreshProviderSelection();
        OnPropertyChanged(nameof(IsOpenAi));
        OnPropertyChanged(nameof(IsAzure));
        OnPropertyChanged(nameof(IsGrok));
#pragma warning disable CS0618
        OnPropertyChanged(nameof(SelectedProvider));
#pragma warning restore CS0618
        OnPropertyChanged(nameof(SelectedProviderDefinition));
        OnPropertyChanged(nameof(ApiKeySectionTitle));
        OnPropertyChanged(nameof(ApiKeyHelpText));
        OnPropertyChanged(nameof(ShowCredentialNotice));
        OnPropertyChanged(nameof(RealtimeModelOptions));
        OnPropertyChanged(nameof(ChatModelOptions));
        OnPropertyChanged(nameof(VisionModelOptions));
        OnPropertyChanged(nameof(TranscriptionModelOptions));
        OnPropertyChanged(nameof(SelectedRealtimeModel));
        OnPropertyChanged(nameof(SelectedChatModel));
        OnPropertyChanged(nameof(SelectedVisionModel));
        OnPropertyChanged(nameof(SelectedTranscriptionModel));
        LoadApiKeyDisplay();
    }

    private void RefreshProviderSelection()
    {
        var selectedId = SelectedProviderId;
        foreach (var option in ProviderOptions)
            option.IsSelected = option.Id == selectedId;
    }

    // --- Model Picker Options ---

    public ModelInfo[] RealtimeModelOptions => _providerRegistry.GetModels(SelectedProviderId, AiModelKind.Realtime);
    public ModelInfo[] ChatModelOptions => _providerRegistry.GetModels(SelectedProviderId, AiModelKind.Chat);
    public ModelInfo[] VisionModelOptions => _providerRegistry.GetModels(SelectedProviderId, AiModelKind.Vision);
    public ModelInfo[] TranscriptionModelOptions => _providerRegistry.GetModels(SelectedProviderId, AiModelKind.Transcription);

    // --- Model Selections ---

    public ModelInfo? SelectedRealtimeModel
    {
        get => RealtimeModelOptions.FirstOrDefault(m => m.Id == _settings.RealtimeModel);
        set
        {
            if (value is not null)
                SetProperty(_settings.RealtimeModel, value.Id, v => _settings.RealtimeModel = v);
        }
    }

    public ModelInfo? SelectedChatModel
    {
        get => ChatModelOptions.FirstOrDefault(m => m.Id == _settings.ChatModel);
        set
        {
            if (value is not null)
                SetProperty(_settings.ChatModel, value.Id, v => _settings.ChatModel = v);
        }
    }

    public ModelInfo? SelectedVisionModel
    {
        get => VisionModelOptions.FirstOrDefault(m => m.Id == _settings.VisionModel);
        set
        {
            if (value is not null)
                SetProperty(_settings.VisionModel, value.Id, v => _settings.VisionModel = v);
        }
    }

    public ModelInfo? SelectedTranscriptionModel
    {
        get => TranscriptionModelOptions.FirstOrDefault(m => m.Id == _settings.TranscriptionModel);
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
        _fullKey = await _apiKeyService.GetApiKeyAsync(SelectedProviderId);
        ApiKeyDisplay = MaskKey(_fullKey);
    }

    private async Task ChangeApiKeyAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;

        var provider = SelectedProviderDefinition;
        var key = await page.DisplayPromptAsync(
            $"{provider.DisplayName} API Key",
            GetApiKeyPrompt(provider),
            placeholder: GetApiKeyPlaceholder(provider),
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (!string.IsNullOrWhiteSpace(key))
        {
            await _apiKeyService.SetApiKeyAsync(SelectedProviderId, key);
            _fullKey = key;
            IsKeyVisible = false;
            ApiKeyDisplay = MaskKey(key);
        }
    }

    private async Task ClearApiKeyAsync()
    {
        await _apiKeyService.ClearApiKeyAsync(SelectedProviderId);
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

    private string _transcriptionStatus = string.Empty;
    public string TranscriptionStatus
    {
        get => _transcriptionStatus;
        set => SetProperty(ref _transcriptionStatus, value);
    }

    public ICommand TestConnectionCommand { get; }

    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        ConnectionStatus = "Testing...";
        RealtimeStatus = ChatStatus = VisionStatus = TranscriptionStatus = string.Empty;

        try
        {
            var provider = SelectedProviderDefinition;
            var key = await _apiKeyService.GetApiKeyAsync(provider.Id);
            if (string.IsNullOrEmpty(key))
            {
                ConnectionStatus = $"✗ No {provider.DisplayName} API key configured";
                return;
            }

            using var http = _httpClientFactory();

            if (provider.Id == AiProviderIds.AzureOpenAi)
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
                TranscriptionStatus = "—";
            }
            else if (provider.Id == AiProviderIds.OpenAi)
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
                TranscriptionStatus = modelIds.Contains(_settings.TranscriptionModel) ? "✓" : "✗ not found";
            }
            else if (provider.Id == AiProviderIds.XaiGrok)
            {
                ChatStatus = VisionStatus = TranscriptionStatus = "—";
                RealtimeStatus = provider.CredentialPolicy.RequiresEphemeralRealtimeTokenBroker
                    ? "— broker required"
                    : "—";
                ConnectionStatus = "✓ Grok API key configured. OAuth is not official for xAI inference yet.";
                return;
            }
            else
            {
                ConnectionStatus = $"✗ {provider.DisplayName} connection test is not implemented yet";
                return;
            }

            var statuses = new[] { RealtimeStatus, ChatStatus, VisionStatus, TranscriptionStatus };
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

    private static string GetApiKeyPrompt(AiProviderDefinition provider) =>
        provider.Id == AiProviderIds.XaiGrok
            ? "Enter your xAI API key. Official xAI inference docs currently use bearer API keys; OAuth sign-in is not available for this app yet."
            : "Enter your OpenAI or Azure OpenAI API key:";

    private static string GetApiKeyPlaceholder(AiProviderDefinition provider) =>
        provider.Id == AiProviderIds.XaiGrok
            ? "xai-..."
            : "sk-proj-... or Azure key";

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

public sealed class ProviderOptionViewModel : ViewModelBase
{
    private bool _isSelected;

    public ProviderOptionViewModel(AiProviderDefinition provider)
    {
        Id = provider.Id;
        DisplayName = provider.DisplayName;
        Description = provider.Description;
        IsEnabled = provider.IsSelectable;
        AutomationId = $"Provider{provider.Id.Replace("-", string.Empty, StringComparison.Ordinal)}Radio";
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool IsEnabled { get; }
    public string AutomationId { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
