# Step 10: Camera TCS Cleanup in PhoneCameraProvider

**Priority:** P2 | **Effort:** Trivial | **Risk:** Event handler leak on capture timeout

---

## Problem

In `PhoneCameraProvider.CaptureViaEventAsync`, if the timeout fires, `tcs.TrySetResult(null)` is called via the cancellation token registration, but the method returns from `await tcs.Task`. The `finally` block properly unsubscribes `MediaCaptured`. However, the `CancellationTokenRegistration` from `timeoutCts.Token.Register(...)` is not disposed.

Current code:

```csharp
using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
timeoutCts.Token.Register(() => tcs.TrySetResult(null));
```

The `Register` returns a `CancellationTokenRegistration` that should be disposed.

## Steps

### 10.1 Dispose the CancellationTokenRegistration

**File:** `src/BodyCam/Services/Camera/PhoneCameraProvider.cs`

In `CaptureViaEventAsync`, change:

```csharp
// Before
timeoutCts.Token.Register(() => tcs.TrySetResult(null));

// After
using var registration = timeoutCts.Token.Register(() => tcs.TrySetResult(null));
```

The `using` ensures the registration is disposed when `CaptureViaEventAsync` completes.

### 10.2 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```
