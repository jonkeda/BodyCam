# M44 Phase 1 - Command Contracts And Defaults

Goal: create the registered command architecture and connect Look first so UI
buttons, physical buttons, wake words, keyboard shortcuts, and LLM tool calls
can run the same command pipeline without central switch statements.

M44 does not need backward compatibility with the current command wiring. During
Phase 1 and Phase 2, the old Read and Scan flows can be disconnected. They come
back later as new registered command classes in Phase 3 and Phase 4.

## Scope

- Add `CameraCommandRequest`.
- Add `ICameraCommand`.
- Add Look command-specific option records.
- Add `ICameraCommandRegistry`.
- Add an `ICameraCommandService`.
- Add `LookCommand` as the first registered command.
- Move command-mode decisions out of individual UI command handlers.
- Add settings for command defaults.
- Disconnect or hide old Read and Scan entry points until their new command
  classes exist.
- Remove old LLM tool registrations for Read and Scan during Phase 1/2 if they
  would otherwise call the legacy path.
- Do not add a `CameraCommandKind` enum for Look/Read/Scan.
- Do not build execution around a large `switch` on command type.

## Compatibility Decision

This milestone is allowed to break the old command APIs and registrations. Do
not build compatibility adapters just to keep the existing `ReadTextTool` or
`ScanQrCodeTool` behavior available.

For Phase 1/2:

- `look` is the only camera command that must be connected to the new pipeline;
- old Read and Scan UI actions can be hidden, disabled, or shown as coming later;
- old Read and Scan LLM tools can be unregistered;
- `ReadCommand` and `ScanCommand` are not registered until their own phases.

## Proposed Contracts

```csharp
public interface ICameraCommand
{
    string Id { get; }
    string DisplayName { get; }
    string? ToolName { get; }
    CameraCommandCapabilities Capabilities { get; }
    IReadOnlyList<CommandOptionDefinition> Options { get; }

    CameraCommandMode ResolveMode(CameraCommandRequest request, CameraCommandContext context);
    Task<CameraCommandResult> ExecuteAsync(CameraCommandContext context, CancellationToken ct);
}

public interface ICameraCommandRegistry
{
    IReadOnlyList<ICameraCommand> Commands { get; }
    bool TryGet(string id, out ICameraCommand command);
    bool TryGetTool(string toolName, out ICameraCommand command);
    ICameraCommand GetRequired(string id);
}

public interface ICameraCommandService
{
    Task<CameraCommandResult> ExecuteAsync(
        CameraCommandRequest request,
        CancellationToken ct = default);
}

public sealed record CameraCommandRequest(
    string CommandId,
    CameraCommandMode? Mode,
    CommandTriggerOrigin Origin,
    object? Options,
    string? Query);

public sealed record CameraCommandContext(
    CameraCommandRequest Request,
    CameraCommandMode ResolvedMode,
    CameraManager Cameras,
    ISettingsService Settings,
    Func<CancellationToken, Task<byte[]?>> CaptureFrame,
    Func<CancellationToken, Task<byte[]?>> WaitForManualCapture);

public sealed record CommandOptionDefinition(
    string Name,
    Type ValueType,
    object? DefaultValue,
    bool PersistLastSelectedValue);

public sealed record CameraCommandResult(
    string CommandId,
    bool Success,
    string TranscriptText,
    object? Data,
    string? Error);
```

The service owns:

- mode resolution;
- cross-cutting default option resolution;
- active camera selection through `CameraManager`;
- capture flow selection;
- command lookup;
- calling the command's `ExecuteAsync`.

The command class owns:

- command-specific options;
- prompts;
- command-specific capture requirements;
- command-specific execution;
- transcript-ready result formatting;
- tool metadata if the command is available to the LLM.

The UI owns:

- showing inline preview when manual aim is selected;
- collecting the final capture button press;
- presenting confirmation prompts for scan actions.

## Default Resolution

Default mode:

| Origin | Mode |
| --- | --- |
| `LlmToolCall` | `FullAuto` |
| `PhysicalButton` | `FullAuto` |
| `WakeWord` | `FullAuto` |
| `KeyboardShortcut` | `FullAuto` |
| `Automation` | request must specify mode |
| `ExplicitManual` | `ManualAim` |
| `ActionsDrawer` | setting, default `ManualAim` on touch devices |

Default detail should be command-specific. Phase 1 only needs the Look default;
the Read and Scan rows describe where later phases should land.

| Command | Default detail |
| --- | --- |
| Look | `Summary` |
| Read | `Full` |
| Scan | not applicable, but result summaries should be concise |

These detail levels should live on command-specific option models, not on one
global enum. For example:

```csharp
public sealed record LookCommandOptions(
    LookDetailLevel DetailLevel,
    string? Focus,
    string? Question);

public enum LookDetailLevel
{
    Summary,
    Overview,
    Detailed,
    Full
}

public sealed record ReadCommandOptions(
    ReadDetailLevel DetailLevel,
    string? Focus);

public enum ReadDetailLevel
{
    Summary,
    Overview,
    Full
}
```

## Settings

Add Phase 1 settings such as:

```csharp
public CameraCommandMode DefaultTouchCommandMode { get; set; } = CameraCommandMode.ManualAim;
public LookDetailLevel DefaultLookDetailLevel { get; set; } = LookDetailLevel.Summary;
```

Phase 3 and Phase 4 can add command-specific settings such as:

```csharp
public ReadDetailLevel DefaultReadDetailLevel { get; set; } = ReadDetailLevel.Full;
public bool ConfirmExternalScanActions { get; set; } = true;
```

Store these through `ISettingsService` so the last user choices persist.

## Command Registration

Register the initial command architecture as DI services:

```csharp
services.AddSingleton<ICameraCommand, LookCommand>();
services.AddSingleton<ICameraCommandRegistry, CameraCommandRegistry>();
services.AddSingleton<ICameraCommandService, CameraCommandService>();
```

Read and Scan are added later:

```csharp
// Phase 3
services.AddSingleton<ICameraCommand, ReadCommand>();

// Phase 4
services.AddSingleton<ICameraCommand, ScanCommand>();
```

The registry should build an immutable lookup by `Id` and optionally by
`ToolName`.

Example:

```csharp
public sealed class CameraCommandRegistry : ICameraCommandRegistry
{
    private readonly IReadOnlyDictionary<string, ICameraCommand> _byId;
    private readonly IReadOnlyDictionary<string, ICameraCommand> _byToolName;

    public CameraCommandRegistry(IEnumerable<ICameraCommand> commands)
    {
        Commands = commands.OrderBy(c => c.DisplayName).ToArray();
        _byId = Commands.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        _byToolName = Commands
            .Where(c => !string.IsNullOrWhiteSpace(c.ToolName))
            .ToDictionary(c => c.ToolName!, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ICameraCommand> Commands { get; }

    public bool TryGet(string id, out ICameraCommand command) =>
        _byId.TryGetValue(id, out command!);

    public bool TryGetTool(string toolName, out ICameraCommand command) =>
        _byToolName.TryGetValue(toolName, out command!);

    public ICameraCommand GetRequired(string id) =>
        TryGet(id, out var command)
            ? command
            : throw new InvalidOperationException($"Unknown camera command '{id}'.");
}
```

## No Central Switches

`CameraCommandService` should not know about Look, Read, or Scan concretely.

Expected flow:

```csharp
var command = registry.GetRequired(request.CommandId);
var mode = command.ResolveMode(request, context);
var result = await command.ExecuteAsync(context with { ResolvedMode = mode }, ct);
```

Adding a new command should not require editing the service. It should only
require:

1. add command options;
2. add command class;
3. register the command;
4. add UI metadata if it should appear in Actions.

## LLM Tool Registration

During Phase 1/2, only Look needs to be exposed as an LLM-visible camera tool.
The adapter should build a command request and call `ICameraCommandService`.

- `look` builds `CameraCommandRequest("look", FullAuto, LlmToolCall, ...)`.

Do not keep `read_text` or `scan_qr_code` registered as wrappers over the old
implementation. They can return in Phase 3/4 when `ReadCommand` and
`ScanCommand` exist. Tool names may be reused then if they still make sense, but
that is not a backward compatibility requirement.

## Acceptance

- Look executes through `ICameraCommandService`.
- Look is a separate registered command class.
- Old Read and Scan execution paths are disconnected, hidden, or disabled.
- No compatibility adapter keeps the old Read or Scan behavior alive.
- Trigger origin is available to the command service.
- Button and LLM invocation of Look capture immediately.
- Manual aim can only happen when explicitly requested or selected by the UI
  default.
- Adding a command does not require editing a central switch statement.
- Unit tests cover Look default mode and detail resolution.
