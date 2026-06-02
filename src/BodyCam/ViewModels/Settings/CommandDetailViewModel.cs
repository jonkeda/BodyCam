using System.Collections.ObjectModel;
using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.Camera.Commands;

namespace BodyCam.ViewModels.Settings;

public sealed class CommandDetailViewModel : ViewModelBase
{
    private readonly ICameraCommandRegistry _registry;
    private readonly ISettingsService _settings;
    private string _commandId = string.Empty;
    private string _toolText = string.Empty;
    private bool _hasLookSettings;
    private bool _hasReadSettings;
    private bool _hasScanSettings;
    private bool _hasPromptDefinitions;
    private bool _hasNoPromptDefinitions;

    public CommandDetailViewModel(ICameraCommandRegistry registry, ISettingsService settings)
    {
        _registry = registry;
        _settings = settings;
        Title = "Command";
    }

    public ObservableCollection<CommandPromptPreview> PromptDefinitions { get; } = [];

    public CameraCommandMode[] TouchModeOptions { get; } =
        [CameraCommandMode.ManualAim, CameraCommandMode.FullAuto];

    public LookDetailLevel[] LookDetailOptions { get; } =
        [LookDetailLevel.Overview, LookDetailLevel.Detailed, LookDetailLevel.Summary, LookDetailLevel.Full];

    public ReadDetailLevel[] ReadDetailOptions { get; } =
        [ReadDetailLevel.Summary, ReadDetailLevel.Overview, ReadDetailLevel.Full];

    public string CommandId
    {
        get => _commandId;
        private set => SetProperty(ref _commandId, value);
    }

    public string ToolText
    {
        get => _toolText;
        private set => SetProperty(ref _toolText, value);
    }

    public bool HasLookSettings
    {
        get => _hasLookSettings;
        private set => SetProperty(ref _hasLookSettings, value);
    }

    public bool HasReadSettings
    {
        get => _hasReadSettings;
        private set => SetProperty(ref _hasReadSettings, value);
    }

    public bool HasScanSettings
    {
        get => _hasScanSettings;
        private set => SetProperty(ref _hasScanSettings, value);
    }

    public bool HasPromptDefinitions
    {
        get => _hasPromptDefinitions;
        private set => SetProperty(ref _hasPromptDefinitions, value);
    }

    public bool HasNoPromptDefinitions
    {
        get => _hasNoPromptDefinitions;
        private set => SetProperty(ref _hasNoPromptDefinitions, value);
    }

    public CameraCommandMode SelectedTouchCommandMode
    {
        get => _settings.DefaultTouchCommandMode;
        set => SetProperty(_settings.DefaultTouchCommandMode, value, v => _settings.DefaultTouchCommandMode = v);
    }

    public LookDetailLevel SelectedLookDetailLevel
    {
        get => _settings.DefaultLookDetailLevel;
        set => SetProperty(_settings.DefaultLookDetailLevel, value, v => _settings.DefaultLookDetailLevel = v);
    }

    public ReadDetailLevel SelectedReadDetailLevel
    {
        get => _settings.DefaultReadDetailLevel;
        set => SetProperty(_settings.DefaultReadDetailLevel, value, v => _settings.DefaultReadDetailLevel = v);
    }

    public bool ConfirmExternalScanActions
    {
        get => _settings.ConfirmExternalScanActions;
        set => SetProperty(_settings.ConfirmExternalScanActions, value, v => _settings.ConfirmExternalScanActions = v);
    }

    public void Load(string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId) || !_registry.TryGet(commandId, out var command))
        {
            Title = "Command";
            CommandId = commandId ?? string.Empty;
            ToolText = "Unknown command";
            HasLookSettings = false;
            HasReadSettings = false;
            HasScanSettings = false;
            SetPrompts([]);
            return;
        }

        Title = command.DisplayName;
        CommandId = command.Id;
        ToolText = string.IsNullOrWhiteSpace(command.ToolName)
            ? "No tool binding"
            : $"Tool: {command.ToolName}";

        HasLookSettings = string.Equals(command.Id, "look", StringComparison.OrdinalIgnoreCase);
        HasReadSettings = string.Equals(command.Id, "read", StringComparison.OrdinalIgnoreCase);
        HasScanSettings = string.Equals(command.Id, "scan", StringComparison.OrdinalIgnoreCase);

        SetPrompts(command is ICommandPromptProvider prompts
            ? prompts.PromptDefinitions
            : []);
    }

    private void SetPrompts(IReadOnlyList<CommandPromptDefinition> definitions)
    {
        PromptDefinitions.Clear();

        foreach (var prompt in definitions)
            PromptDefinitions.Add(new CommandPromptPreview(prompt));

        HasPromptDefinitions = PromptDefinitions.Count > 0;
        HasNoPromptDefinitions = !HasPromptDefinitions;
    }
}

public sealed class CommandPromptPreview
{
    public CommandPromptPreview(CommandPromptDefinition definition)
    {
        Key = definition.Key;
        DisplayName = definition.DisplayName;
        Text = definition.Text;
        Prompt = definition.Prompt;
        AutomationId = $"{definition.Key}PromptPreview";
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string Text { get; }
    public string Prompt { get; }
    public string AutomationId { get; }
}
