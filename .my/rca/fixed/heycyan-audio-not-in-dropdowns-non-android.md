# RCA: HeyCyan glasses audio not selectable in dropdowns on Windows/iOS

**Date**: 2026-05-16
**Severity**: Feature gap — HeyCyan glasses audio unusable on non-Android platforms
**Status**: Open

## Symptom

After connecting HeyCyan glasses via BLE, the Microphone and Speaker pickers
still show only "System Microphone" and "System Speaker". The HeyCyan glasses
do not appear as selectable options and are not auto-selected.

**Expected**: HeyCyan glasses should appear in the dropdowns only when connected,
and be auto-selected.

## Root Cause

Four issues — three registration gaps and one missing UI filter:

### Bug 1 — HeyCyanAudioInputProvider not registered as IAudioInputProvider

```csharp
#if ANDROID
    services.AddSingleton<HeyCyanAudioInputProvider>();
    services.AddSingleton<IAudioInputProvider>(sp =>
        sp.GetRequiredService<HeyCyanAudioInputProvider>());        // ← visible to AudioInputManager
#else
    // Register concrete type so HeyCyanGlassesDeviceManager can be resolved
    services.AddSingleton<HeyCyanAudioInputProvider>();             // ← concrete-only, invisible
#endif
```

`AudioInputManager.Providers` is populated from all `IAudioInputProvider`
registrations injected via DI. On Windows/iOS the provider is registered only
as its concrete type, so the manager never sees it and the picker never shows
"HeyCyan Glasses Mic".

### Bug 2 — HeyCyanAudioOutputProvider not registered as IAudioOutputProvider

Same pattern for output:

```csharp
#if ANDROID
    services.AddSingleton<HeyCyanAudioOutputProvider>();
    services.AddSingleton<IAudioOutputProvider>(sp =>
        sp.GetRequiredService<HeyCyanAudioOutputProvider>());       // ← visible
#else
    services.AddSingleton<HeyCyanAudioOutputProvider>();            // ← concrete-only, invisible
#endif
```

### Bug 3 — HeyCyanAudioRouter not registered on Windows/iOS

```csharp
#if ANDROID
    services.AddSingleton<HeyCyanAudioRouter>();                    // ← auto-selects on connect
#elif IOS
    // no router
#else
    // no router
#endif
```

Even if bugs 1 & 2 were fixed, the glasses would appear in the dropdowns but
would never be auto-selected on connection because `HeyCyanAudioRouter` —
which subscribes to `HeyCyanSession.StateChanged` and calls
`SetActiveProviderAsync("heycyan-glasses")` — is only instantiated on Android.

### Bug 4 — No availability filtering on dropdown providers

`AudioInputManager.Providers` and `AudioOutputManager.Providers` return **all**
registered providers unfiltered — `IsAvailable` is never checked. On Android
(where registration works), "HeyCyan Glasses Mic" / "HeyCyan Glasses Speaker"
appear in the dropdowns **all the time**, even when glasses are disconnected.

```csharp
// AudioInputManager.cs:69
public IReadOnlyList<IAudioInputProvider> Providers => _providers.AsReadOnly();

// DeviceViewModel.cs:109 — no filtering
public IReadOnlyList<IAudioInputProvider> AudioInputProviders => _audioInputManager.Providers;
```

`HeyCyanAudioInputProvider.IsAvailable` correctly checks connection state:

```csharp
public bool IsAvailable =>
    _session.State == HeyCyanState.Connected &&
    _bt.HasEndpointWithMac(_session.Device?.Address);
```

…but nothing in the UI pipeline reads it. Selecting an unavailable provider
would fail at `StartAsync`.

## Why It Wasn't Caught

- HeyCyan hardware testing has been Android-only; Windows/iOS testing uses
  platform mic/speaker exclusively.
- The `#if ANDROID` guards were added intentionally during Phase 8 when the
  HeyCyan BT audio stack only worked on Android. The guards were never revisited
  after Windows BT support was added.
- The concrete-only registrations were added for `HeyCyanGlassesDeviceManager`
  DI resolution and look correct at a glance — the missing `IAudioInputProvider`
  / `IAudioOutputProvider` forwarding registrations are easy to overlook.

## Fix

HeyCyan providers cannot be registered as `IAudioInputProvider` / `IAudioOutputProvider`
in DI because of a circular dependency:

```
AudioInputManager → IEnumerable<IAudioInputProvider> → HeyCyanAudioInputProvider
  → IBluetoothAudioInputProvider → BluetoothAudioInputProvider → AudioInputManager
```

This causes a deadlock during singleton resolution (same cycle for output).

Instead, `HeyCyanAudioRouter` dynamically registers/unregisters providers with the
managers on glasses connect/disconnect:

1. Keep `HeyCyanAudioInputProvider` and `HeyCyanAudioOutputProvider` registered as
   **concrete types only** (no interface forwarding) on all platforms.

2. `HeyCyanAudioRouter` constructor now takes the concrete providers and calls
   `RegisterProvider()` / `UnregisterProviderAsync()` on the managers in `ApplyAsync()`.
   On connect: register + set active. On disconnect: unregister (auto-falls back).

3. Register `HeyCyanAudioRouter` on all platforms (not just Android) and remove
   `#if ANDROID` from the eager resolution in `MauiProgram.cs`.

4. `DeviceViewModel.AudioInputProviders` / `AudioOutputProviders` remain unfiltered —
   dynamic registration means glasses only appear in the list when connected.

## Files Changed

- `src/BodyCam/ServiceExtensions.cs` — concrete-only registration on all platforms
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioRouter.cs` — dynamic register/unregister
- `src/BodyCam/MauiProgram.cs` — remove `#if ANDROID` from router resolution
