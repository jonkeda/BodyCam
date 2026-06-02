using System.Windows.Input;
using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.AiProviders;

namespace BodyCam.ViewModels.Settings;

public sealed class LlmProvidersViewModel : ViewModelBase
{
    private readonly IAiProviderInstanceStore _store;
    private readonly IAiProviderRegistry _registry;
    private readonly IApiKeyService _apiKeyService;
    private IReadOnlyList<LlmProviderRowViewModel> _providers = [];

    public LlmProvidersViewModel(
        IAiProviderInstanceStore store,
        IAiProviderRegistry registry,
        IApiKeyService apiKeyService)
    {
        _store = store;
        _registry = registry;
        _apiKeyService = apiKeyService;
        Title = "LLM Providers";
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _ = RefreshAsync();
    }

    public IReadOnlyList<LlmProviderRowViewModel> Providers
    {
        get => _providers;
        private set => SetProperty(ref _providers, value);
    }

    public ICommand RefreshCommand { get; }

    public async Task RefreshAsync()
    {
        var instances = await _store.GetInstancesAsync();
        var rows = new List<LlmProviderRowViewModel>();
        foreach (var instance in instances)
        {
            var provider = _registry.TryGet(instance.ProviderId);
            if (provider is null)
                continue;

            var key = await _apiKeyService.GetApiKeyAsync(provider.Id);
            rows.Add(new LlmProviderRowViewModel(instance, provider, key));
        }

        Providers = rows;
    }
}

public sealed class LlmProviderRowViewModel
{
    public LlmProviderRowViewModel(
        AiProviderInstanceSettings instance,
        AiProviderDefinition provider,
        string? apiKey)
    {
        InstanceId = instance.InstanceId;
        ProviderId = provider.Id;
        DisplayName = instance.DisplayName;
        Description = provider.Description;
        IsActive = instance.IsActive;
        CredentialStatus = string.IsNullOrWhiteSpace(apiKey)
            ? ProviderId == AiProviderIds.XaiGrok ? "API key needed" : "Not configured"
            : "API key configured";
        ActiveStatus = IsActive ? "Active" : "Not active";
        StatusText = $"{ActiveStatus} - {CredentialStatus}";
        CapabilitySummary = CapabilityText.ForProvider(provider);
        AutomationId = $"LlmProvider{ProviderId.Replace("-", string.Empty, StringComparison.Ordinal)}Row";
        EditAutomationId = $"Edit{ProviderId.Replace("-", string.Empty, StringComparison.Ordinal)}ProviderButton";
    }

    public string InstanceId { get; }
    public string ProviderId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool IsActive { get; }
    public string ActiveStatus { get; }
    public string CredentialStatus { get; }
    public string StatusText { get; }
    public string CapabilitySummary { get; }
    public string AutomationId { get; }
    public string EditAutomationId { get; }
}

public sealed class AddLlmProviderViewModel : ViewModelBase
{
    public AddLlmProviderViewModel(IAiProviderRegistry registry)
    {
        Title = "Add LLM Provider";
        ProviderChoices = registry.Providers
            .Where(provider => provider.IsSelectable)
            .Select(provider => new AddLlmProviderChoiceViewModel(provider))
            .ToArray();
    }

    public IReadOnlyList<AddLlmProviderChoiceViewModel> ProviderChoices { get; }
}

public sealed class AddLlmProviderChoiceViewModel
{
    public AddLlmProviderChoiceViewModel(AiProviderDefinition provider)
    {
        ProviderId = provider.Id;
        DisplayName = provider.DisplayName;
        Detail = provider.Id switch
        {
            AiProviderIds.XaiGrok => "xAI account, API key, OAuth when official",
            AiProviderIds.AzureOpenAi => "Azure endpoint and deployment names",
            _ => "OpenAI platform API key"
        };
        AutomationId = $"Add{ProviderId.Replace("-", string.Empty, StringComparison.Ordinal)}ProviderButton";
    }

    public string ProviderId { get; }
    public string DisplayName { get; }
    public string Detail { get; }
    public string AutomationId { get; }
}

public sealed class LlmProviderDetailViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IApiKeyService _apiKeyService;
    private readonly IAiProviderRegistry _registry;
    private readonly IAiProviderInstanceStore _store;
    private readonly IAiProviderDiagnosticsService _diagnosticsService;
    private AiProviderInstanceSettings? _instance;
    private AiProviderDefinition? _provider;
    private string? _fullKey;
    private string _providerId = AiProviderIds.OpenAi;
    private string _displayName = string.Empty;
    private string _description = string.Empty;
    private string _apiKeyDisplay = "(not set)";
    private string _diagnostics = string.Empty;
    private bool _isActive;
    private bool _isKeyVisible;
    private bool _isTesting;
    private IReadOnlyList<ProviderSetupLinkViewModel> _setupLinks = [];
    private IReadOnlyList<CapabilityStatusViewModel> _capabilities = [];

    public LlmProviderDetailViewModel(
        ISettingsService settings,
        IApiKeyService apiKeyService,
        IAiProviderRegistry registry,
        IAiProviderInstanceStore store,
        IAiProviderDiagnosticsService diagnosticsService)
    {
        _settings = settings;
        _apiKeyService = apiKeyService;
        _registry = registry;
        _store = store;
        _diagnosticsService = diagnosticsService;

        SetActiveCommand = new AsyncRelayCommand(SetActiveAsync);
        ChangeApiKeyCommand = new AsyncRelayCommand(ChangeApiKeyAsync);
        ClearApiKeyCommand = new AsyncRelayCommand(ClearApiKeyAsync);
        ToggleKeyVisibilityCommand = new RelayCommand(ToggleKeyVisibility);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        OpenSetupLinkCommand = new AsyncRelayCommand(OpenSetupLinkAsync);
    }

    public string ProviderId
    {
        get => _providerId;
        private set => SetProperty(ref _providerId, value);
    }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    public bool IsOpenAi => ProviderId == AiProviderIds.OpenAi;
    public bool IsAzure => ProviderId == AiProviderIds.AzureOpenAi;
    public bool IsGrok => ProviderId == AiProviderIds.XaiGrok;
    public bool ShowModelPickers => !IsAzure;
    public bool ShowAzureDeployments => IsAzure;
    public bool ShowGrokOnlySettings => IsGrok;

    public string ActiveStatus => IsActive ? "Active provider" : "Not active";

    public string CredentialNotice =>
        _provider?.CredentialPolicy.OAuthAvailable == true
            ? "Browser sign-in is available."
            : _provider?.CredentialPolicy.OAuthUnavailableReason
                ?? "API keys and secrets are stored securely on this device.";

    public string ApiKeyTitle => IsGrok ? "xAI API Key" : "API Key";

    public string ApiKeyDisplay
    {
        get => _apiKeyDisplay;
        private set => SetProperty(ref _apiKeyDisplay, value);
    }

    public bool IsKeyVisible
    {
        get => _isKeyVisible;
        set
        {
            if (SetProperty(ref _isKeyVisible, value))
            {
                OnPropertyChanged(nameof(KeyToggleText));
                ApiKeyDisplay = value ? (_fullKey ?? "(not set)") : MaskKey(_fullKey);
            }
        }
    }

    public string KeyToggleText => IsKeyVisible ? "Hide" : "Show";

    public bool IsTesting
    {
        get => _isTesting;
        private set => SetProperty(ref _isTesting, value);
    }

    public string Diagnostics
    {
        get => _diagnostics;
        private set => SetProperty(ref _diagnostics, value);
    }

    public IReadOnlyList<ProviderSetupLinkViewModel> SetupLinks
    {
        get => _setupLinks;
        private set => SetProperty(ref _setupLinks, value);
    }

    public IReadOnlyList<CapabilityStatusViewModel> Capabilities
    {
        get => _capabilities;
        private set => SetProperty(ref _capabilities, value);
    }

    public ModelInfo[] RealtimeModelOptions => _registry.GetModels(ProviderId, AiModelKind.Realtime);
    public ModelInfo[] ChatModelOptions => _registry.GetModels(ProviderId, AiModelKind.Chat);
    public ModelInfo[] VisionModelOptions => _registry.GetModels(ProviderId, AiModelKind.Vision);
    public ModelInfo[] TranscriptionModelOptions => _registry.GetModels(ProviderId, AiModelKind.Transcription);
    public ModelInfo[] TextToSpeechModelOptions => _registry.GetModels(ProviderId, AiModelKind.TextToSpeech);
    public ModelInfo[] ImageGenerationModelOptions => _registry.GetModels(ProviderId, AiModelKind.ImageGeneration);

    public ModelInfo? SelectedRealtimeModel
    {
        get => RealtimeModelOptions.FirstOrDefault(model => model.Id == _settings.RealtimeModel)
            ?? RealtimeModelOptions.FirstOrDefault();
        set
        {
            if (value is not null)
                SetProperty(_settings.RealtimeModel, value.Id, v => _settings.RealtimeModel = v);
        }
    }

    public ModelInfo? SelectedChatModel
    {
        get => ChatModelOptions.FirstOrDefault(model => model.Id == _settings.ChatModel)
            ?? ChatModelOptions.FirstOrDefault();
        set
        {
            if (value is not null)
                SetProperty(_settings.ChatModel, value.Id, v => _settings.ChatModel = v);
        }
    }

    public ModelInfo? SelectedVisionModel
    {
        get => VisionModelOptions.FirstOrDefault(model => model.Id == _settings.VisionModel)
            ?? VisionModelOptions.FirstOrDefault();
        set
        {
            if (value is not null)
                SetProperty(_settings.VisionModel, value.Id, v => _settings.VisionModel = v);
        }
    }

    public ModelInfo? SelectedTranscriptionModel
    {
        get => TranscriptionModelOptions.FirstOrDefault(model => model.Id == _settings.TranscriptionModel)
            ?? TranscriptionModelOptions.FirstOrDefault();
        set
        {
            if (value is not null)
                SetProperty(_settings.TranscriptionModel, value.Id, v => _settings.TranscriptionModel = v);
        }
    }

    public ModelInfo? SelectedTextToSpeechModel
    {
        get => TextToSpeechModelOptions.FirstOrDefault();
        set { }
    }

    public ModelInfo? SelectedImageGenerationModel
    {
        get => ImageGenerationModelOptions.FirstOrDefault();
        set { }
    }

    public string Voice
    {
        get => _settings.Voice;
        set => SetProperty(_settings.Voice, value, v => _settings.Voice = v);
    }

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

    public ICommand SetActiveCommand { get; }
    public ICommand ChangeApiKeyCommand { get; }
    public ICommand ClearApiKeyCommand { get; }
    public ICommand ToggleKeyVisibilityCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand OpenSetupLinkCommand { get; }

    public async Task LoadProviderAsync(string providerId)
    {
        _instance = await _store.EnsureInstanceAsync(providerId);
        _provider = _registry.GetRequired(_instance.ProviderId);
        ProviderId = _provider.Id;
        DisplayName = _provider.DisplayName;
        Description = _provider.Description;
        Title = _provider.DisplayName;
        IsActive = _instance.IsActive;
        SetupLinks = _provider.SetupLinks.Select(link => new ProviderSetupLinkViewModel(link)).ToArray();
        Capabilities = BuildCapabilities(_provider);
        await LoadApiKeyDisplayAsync();
        NotifyProviderPropertiesChanged();
    }

    private async Task SetActiveAsync()
    {
        if (_instance is null)
            return;

        await _store.SetActiveAsync(_instance.InstanceId);
        _settings.ProviderId = ProviderId;
        IsActive = true;
        Diagnostics = $"{DisplayName} is now active.";
        OnPropertyChanged(nameof(ActiveStatus));
    }

    private async Task ChangeApiKeyAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null || _provider is null)
            return;

        var key = await page.DisplayPromptAsync(
            $"{_provider.DisplayName} API Key",
            ProviderId == AiProviderIds.XaiGrok
                ? "Enter your xAI API key. Official OAuth is not available for xAI inference in this app yet."
                : "Enter the API key for this provider.",
            placeholder: ProviderId == AiProviderIds.XaiGrok ? "xai-..." : "sk-proj-... or Azure key",
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(key))
            return;

        await _apiKeyService.SetApiKeyAsync(ProviderId, key);
        _fullKey = key;
        IsKeyVisible = false;
        ApiKeyDisplay = MaskKey(key);
    }

    private async Task ClearApiKeyAsync()
    {
        await _apiKeyService.ClearApiKeyAsync(ProviderId);
        _fullKey = null;
        IsKeyVisible = false;
        ApiKeyDisplay = "(not set)";
    }

    private void ToggleKeyVisibility() => IsKeyVisible = !IsKeyVisible;

    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        try
        {
            var result = await _diagnosticsService.TestAsync(ProviderId);
            Diagnostics = FormatDiagnostics(result);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private static async Task OpenSetupLinkAsync(object? parameter)
    {
        if (parameter is ProviderSetupLinkViewModel link)
            await Launcher.Default.OpenAsync(link.Url);
    }

    private static string FormatDiagnostics(AiProviderDiagnosticResult result)
    {
        if (result.Capabilities.Count == 0)
            return result.Summary;

        var lines = new List<string> { result.Summary };
        lines.AddRange(result.Capabilities.Select(capability =>
            $"{capability.Capability}: {capability.Status} - {capability.Message}"));
        return string.Join(Environment.NewLine, lines);
    }

    private async Task LoadApiKeyDisplayAsync()
    {
        _fullKey = await _apiKeyService.GetApiKeyAsync(ProviderId);
        ApiKeyDisplay = MaskKey(_fullKey);
    }

    private static string MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "(not set)";
        if (key.Length <= 8)
            return "****";
        return key[..4] + "****" + key[^4..];
    }

    private static CapabilityStatusViewModel[] BuildCapabilities(AiProviderDefinition provider) =>
    [
        new("Text", provider.Supports(AiProviderCapability.Chat)),
        new("Voice", provider.Supports(AiProviderCapability.RealtimeVoice)),
        new("STT", provider.Supports(AiProviderCapability.SpeechToText)),
        new("TTS", provider.Supports(AiProviderCapability.TextToSpeech)),
        new("Vision", provider.Supports(AiProviderCapability.Vision) || provider.Supports(AiProviderCapability.ImageInput)),
        new("Images", provider.Supports(AiProviderCapability.ImageGeneration) || provider.Supports(AiProviderCapability.ImageEditing)),
    ];

    private void NotifyProviderPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsOpenAi));
        OnPropertyChanged(nameof(IsAzure));
        OnPropertyChanged(nameof(IsGrok));
        OnPropertyChanged(nameof(ShowModelPickers));
        OnPropertyChanged(nameof(ShowAzureDeployments));
        OnPropertyChanged(nameof(ShowGrokOnlySettings));
        OnPropertyChanged(nameof(ActiveStatus));
        OnPropertyChanged(nameof(CredentialNotice));
        OnPropertyChanged(nameof(ApiKeyTitle));
        OnPropertyChanged(nameof(RealtimeModelOptions));
        OnPropertyChanged(nameof(ChatModelOptions));
        OnPropertyChanged(nameof(VisionModelOptions));
        OnPropertyChanged(nameof(TranscriptionModelOptions));
        OnPropertyChanged(nameof(TextToSpeechModelOptions));
        OnPropertyChanged(nameof(ImageGenerationModelOptions));
        OnPropertyChanged(nameof(SelectedRealtimeModel));
        OnPropertyChanged(nameof(SelectedChatModel));
        OnPropertyChanged(nameof(SelectedVisionModel));
        OnPropertyChanged(nameof(SelectedTranscriptionModel));
        OnPropertyChanged(nameof(SelectedTextToSpeechModel));
        OnPropertyChanged(nameof(SelectedImageGenerationModel));
    }
}

public sealed class ProviderSetupLinkViewModel
{
    public ProviderSetupLinkViewModel(AiProviderSetupLink link)
    {
        Label = link.Label;
        Url = link.Url;
        Kind = link.Kind.ToString();
        AutomationId = $"Open{Kind}{Label.Replace(" ", string.Empty, StringComparison.Ordinal)}Button";
    }

    public string Label { get; }
    public Uri Url { get; }
    public string Kind { get; }
    public string AutomationId { get; }
}

public sealed class CapabilityStatusViewModel
{
    public CapabilityStatusViewModel(string name, bool supported)
    {
        Name = name;
        Status = supported ? "Supported" : "Not supported";
    }

    public string Name { get; }
    public string Status { get; }
}

internal static class CapabilityText
{
    public static string ForProvider(AiProviderDefinition provider)
    {
        var values = new List<string>();
        if (provider.Supports(AiProviderCapability.Chat))
            values.Add("Text");
        if (provider.Supports(AiProviderCapability.RealtimeVoice))
            values.Add("Voice");
        if (provider.Supports(AiProviderCapability.Vision) || provider.Supports(AiProviderCapability.ImageInput))
            values.Add("Vision");
        if (provider.Supports(AiProviderCapability.ImageGeneration))
            values.Add("Images");

        return values.Count == 0 ? "No app capabilities" : string.Join(", ", values);
    }
}
