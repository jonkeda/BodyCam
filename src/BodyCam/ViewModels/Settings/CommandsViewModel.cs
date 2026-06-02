using System.Collections.ObjectModel;
using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.Camera.Commands;

namespace BodyCam.ViewModels.Settings;

public sealed class CommandsViewModel : ViewModelBase
{
    public CommandsViewModel(ICameraCommandRegistry registry, ISettingsService settings)
    {
        Title = "Commands";

        foreach (var command in registry.Commands)
            Commands.Add(CommandListItem.From(command, settings));
    }

    public ObservableCollection<CommandListItem> Commands { get; } = [];
}

public sealed class CommandListItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Summary { get; init; }
    public required string ToolText { get; init; }
    public required string AutomationId { get; init; }

    public static CommandListItem From(ICameraCommand command, ISettingsService settings)
    {
        var summary = command.Id switch
        {
            "look" => $"Default: {settings.DefaultLookDetailLevel}",
            "read" => $"Default: {settings.DefaultReadDetailLevel}",
            "scan" => $"Confirm external actions: {(settings.ConfirmExternalScanActions ? "On" : "Off")}",
            _ => command.Capabilities.RequiresStillFrame ? "Uses camera frame" : "Command"
        };

        return new CommandListItem
        {
            Id = command.Id,
            DisplayName = command.DisplayName,
            Summary = summary,
            ToolText = string.IsNullOrWhiteSpace(command.ToolName)
                ? "No tool binding"
                : $"Tool: {command.ToolName}",
            AutomationId = $"{command.Id}CommandRow"
        };
    }
}
