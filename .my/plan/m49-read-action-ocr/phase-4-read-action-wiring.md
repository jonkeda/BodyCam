# M49 Phase 4 - Read Action Wiring

**Status:** Proposed
**Goal:** Connect the OCR-backed implementation to the existing Read action
surface, without adding a second user-facing OCR action.

## Current Behavior

Read already has a single user-facing action path:

| Layer | File | Current identity |
| --- | --- | --- |
| Assistive action ID | `src/BodyCam/Services/Actions/AssistiveActionIds.cs` | `camera.read` |
| Assistive action registration | `src/BodyCam/ServiceExtensions.cs` | `new CameraAssistiveAction(AssistiveActionIds.Read, "Read", "read", ...)` |
| Camera command | `src/BodyCam/Services/Camera/Commands/ReadCommand.cs` | `Id = "read"` |
| LLM tool | `src/BodyCam/Tools/ReadTextTool.cs` | `Name = "read_text"` delegates to command ID `read` |

M49 should keep that shape.

## Target Rule

There should be one user-visible Read action.

```
Actions drawer / button / wake word / read_text tool
  -> camera.read
  -> CameraAssistiveAction
  -> command ID "read"
  -> ReadCommand
  -> IReadOcrService
```

If the implementation uses an internal name like `read-ocr`, keep it below the
command boundary. It can be an internal service, analytics capability path, or
diagnostic label, but it should not become:

- a second assistive action ID;
- a second actions drawer item;
- a second wake word;
- a second LLM tool;
- a parallel camera command beside `read`.

## Implementation Checks

### Assistive Action Registration

Keep the existing registration in `ServiceExtensions.AddCameraServices()`:

```csharp
services.AddSingleton<IAssistiveAction>(sp =>
    new CameraAssistiveAction(
        AssistiveActionIds.Read,
        "Read",
        "read",
        sp.GetRequiredService<ICameraCommandService>()));
```

Do not add `AssistiveActionIds.ReadOcr` unless product UX explicitly needs a
separate visible action later.

### Command Registry

Keep one registered `ReadCommand`:

```csharp
services.AddSingleton<ICameraCommand, ReadCommand>();
```

`ReadCommand` owns the OCR backend selection. The registry should not need to
know whether Read is powered by a vision prompt, plugin OCR, or a future OCR
engine.

### Tool Path

Keep `ReadTextTool` as the LLM-facing function name:

```csharp
public override string Name => "read_text";
```

The tool should continue creating `CameraCommandRequest("read", ...)`. Do not
create a `read_ocr` tool unless the old `read_text` API is deliberately being
retired.

### Analytics

Use action and command identities separately:

- `action.id = camera.read`
- `command = read`
- `capability.path = local_ocr`
- `ocr.engine = plugin_maui_ocr`

This makes it clear that OCR is the implementation path for Read, not a second
action.

## Acceptance

- The actions drawer still shows one Read action.
- `AssistiveActionIds.Read` remains `camera.read`.
- `CameraAssistiveAction` for Read still invokes command ID `read`.
- `ReadTextTool` still invokes command ID `read`.
- `ReadCommand` uses `IReadOcrService`.
- No user-facing `read-ocr`, `read_ocr`, or `ocr_read` action/tool/command is
  registered.

## Risks

- Adding a second action for implementation convenience would split settings,
  tests, analytics, and button mappings.
- Renaming `read_text` would break the existing LLM tool surface for no user
  benefit.
- Putting OCR logic in the action layer would bypass command modes and manual
  capture behavior. OCR should remain inside `ReadCommand`.
