# Step 05 — Delete Dead Code

Delete the hand-rolled Realtime client, its interface, DTOs, and JSON source generator. Also remove the temporary old registration from DI.

**Depends on:** Steps 02, 03, 04 (nothing references these files anymore)  
**Touches:** 4 file deletions + `ServiceExtensions.cs`  
**Tests affected:** `RealtimeMessageTests.cs`, `RealtimeModelsTests.cs` (step 06)

---

## What to Do

### 5.1 — Delete files

| File | Lines | Reason |
|---|---|---|
| `src/BodyCam/Services/IRealtimeClient.cs` | 47 | Interface replaced by `Microsoft.Extensions.AI.IRealtimeClient` |
| `src/BodyCam/Services/RealtimeClient.cs` | 375 | WebSocket + receive loop + JSON dispatch — all replaced by MAF |
| `src/BodyCam/Services/Realtime/RealtimeMessages.cs` | ~200 | 20+ DTOs with `[JsonPropertyName]` — SDK handles serialization |
| `src/BodyCam/Services/Realtime/RealtimeJsonContext.cs` | 16 | Source-gen JSON context — no longer needed |

```powershell
Remove-Item src/BodyCam/Services/IRealtimeClient.cs
Remove-Item src/BodyCam/Services/RealtimeClient.cs
Remove-Item src/BodyCam/Services/Realtime/RealtimeMessages.cs
Remove-Item src/BodyCam/Services/Realtime/RealtimeJsonContext.cs
```

### 5.2 — Remove `Realtime/` directory

If empty after deletions:

```powershell
Remove-Item src/BodyCam/Services/Realtime -Recurse -ErrorAction SilentlyContinue
```

### 5.3 — Remove old DI registration

```
src/BodyCam/ServiceExtensions.cs — AddOrchestration()
```

**Remove this line** (the temporary fully-qualified registration from step 01):
```csharp
services.AddSingleton<BodyCam.Services.IRealtimeClient, BodyCam.Services.RealtimeClient>();
```

### 5.4 — Clean up usings

Remove `using BodyCam.Services.Realtime;` from any file that still has it. After the deletions, the namespace no longer exists.

Check:
- `src/BodyCam/Services/RealtimeClient.cs` is deleted, so no concern
- Grep for remaining references:
```powershell
Get-ChildItem src/BodyCam -Recurse -Filter *.cs | Select-String "using BodyCam.Services.Realtime"
Get-ChildItem src/BodyCam -Recurse -Filter *.cs | Select-String "BodyCam.Services.IRealtimeClient"
```

### 5.5 — Verify no remaining references

```powershell
# Should return nothing after cleanup
Get-ChildItem src/ -Recurse -Filter *.cs | Select-String "IRealtimeClient" | Where-Object { $_.Line -notmatch "Microsoft.Extensions.AI" }
```

Expected survivors:
- `GlobalUsings.cs` in test project (cleaned in step 06)
- Test files with `Substitute.For<IRealtimeClient>()` (cleaned in step 06)

---

## Lines Deleted

| File | Lines |
|---|---|
| `IRealtimeClient.cs` | 47 |
| `RealtimeClient.cs` | 375 |
| `RealtimeMessages.cs` | ~200 |
| `RealtimeJsonContext.cs` | 16 |
| **Total** | **~638** |

---

## Acceptance Criteria

- [ ] All 4 files deleted
- [ ] `Services/Realtime/` directory removed
- [ ] Old `IRealtimeClient, RealtimeClient` registration removed from DI
- [ ] No `using BodyCam.Services.Realtime` anywhere in `src/BodyCam/`
- [ ] No reference to `BodyCam.Services.IRealtimeClient` in `src/BodyCam/`
- [ ] Windows build succeeds (tests won't compile until step 06)
