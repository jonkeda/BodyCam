# Step 07 — Build Verification + Smoke Test

Final verification that everything compiles, all tests pass, and the app works end-to-end.

**Depends on:** Steps 01–06  
**Touches:** Nothing — read-only verification

---

## What to Do

### 7.1 — Windows build

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
```

Must succeed with zero errors and zero warnings related to the migration.

### 7.2 — Android build

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android
```

Must succeed.

### 7.3 — Full test suite

```powershell
dotnet test src/BodyCam.Tests/ --verbosity normal
```

All tests must pass. Expected: ~370 tests (previous 372 minus deleted `RealtimeMessageTests`).

### 7.4 — Manual smoke test (Windows)

Run the app and verify:

1. **Launch** — app starts without DI errors
2. **Start session** — verify "Realtime session created" appears in logs
3. **Speak** — verify your voice is transcribed ("You: ...")
4. **AI response** — verify AI response audio plays through speakers
5. **AI transcript** — verify AI response text appears ("AI: ...")
6. **Interruption** — speak while AI is talking, verify audio cuts off
7. **Function call** — say "describe what you see" with camera active, verify tool executes automatically (no manual function call plumbing)
8. **Stop session** — verify clean shutdown, no orphaned tasks
9. **Reconnection** — kill network briefly, verify auto-reconnect

### 7.5 — Manual smoke test (Android)

Deploy to device/emulator:

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -t:Install
```

Same test sequence as Windows (items 1-8). Skip item 9 (reconnection) if testing on emulator.

### 7.6 — Verify deleted code

Confirm the dead code is actually gone:

```powershell
# Should return empty
Get-ChildItem src/BodyCam/Services -Filter "IRealtimeClient.cs" -Recurse
Get-ChildItem src/BodyCam/Services -Filter "RealtimeClient.cs" -Recurse
Test-Path src/BodyCam/Services/Realtime

# Should return only Microsoft.Extensions.AI references
Get-ChildItem src/ -Recurse -Filter *.cs | Select-String "IRealtimeClient" | Format-Table Path, Line
```

### 7.7 — Verify middleware pipeline

Check logs during smoke test for evidence that middleware is working:

- **`FunctionInvokingRealtimeClient`**: When a tool is called, you should see the middleware log the function invocation automatically — no "Function call: {ToolName}" from our orchestrator code.
- **`LoggingRealtimeClient`**: All sent/received messages should appear in structured logs.

---

## Migration Metrics

| Metric | Before | After |
|---|---|---|
| Custom Realtime code | ~700 lines (4 files) | 0 |
| Event subscriptions | 22 (11 subscribe + 11 unsubscribe) | 0 |
| `async void` handlers | 5 in orchestrator | 0 |
| Manual JSON DTOs | 20+ records | 0 |
| Manual function call pipeline | ~30 lines (receive → dispatch → execute → respond) | 0 (middleware) |
| Schema break risk | Every API change | NuGet update handles it |
| `ToolContext.RealtimeClient` | Required property (unused) | Removed |
| Test files updated | — | 20+ |

---

## Acceptance Criteria

- [ ] Windows build succeeds
- [ ] Android build succeeds
- [ ] All tests pass
- [ ] Windows smoke test passes (all 9 items)
- [ ] Android smoke test passes
- [ ] No references to deleted files remain
- [ ] Middleware pipeline logs visible
- [ ] No regressions in existing functionality

---

## Rollback Plan

If the migration causes issues that can't be resolved quickly:

1. `git stash` or create a branch before starting
2. All changes are in `src/BodyCam/` and `src/BodyCam.Tests/` — no infrastructure changes
3. The old `RealtimeClient.cs` and friends can be restored from git history
4. The NuGet packages (`Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`) remain — they don't conflict

## Post-Migration Cleanup

After the migration is verified stable:

1. Remove `Models/RealtimeSessionConfig.cs` if no longer used (was for the hand-rolled client's session update)
2. Consider renaming `Models/RealtimeModels.cs` types if naming conflicts arise
3. Update `docs/architecture.md` to reflect the new MAF-based architecture
4. Update `docs/services.md` to remove the hand-rolled Realtime client documentation
