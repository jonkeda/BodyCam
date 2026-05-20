using BodyCam.Models;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services;

/// <summary>
/// Orchestrates the source profile system. Sits between DeviceViewModel and the
/// three managers (CameraManager, AudioInputManager, AudioOutputManager).
/// </summary>
public sealed class SourceProfileManager
{
    private readonly IReadOnlyList<ISourceProfile> _profiles;
    private readonly CameraManager _camera;
    private readonly AudioInputManager _mic;
    private readonly AudioOutputManager _speaker;
    private readonly ISettingsService _settings;
    private readonly ILogger<SourceProfileManager> _log;

    private ISourceProfile? _activeProfile;
    private bool _lastSwitchWasManual;

    /// <summary>Fires when the active profile changes (for ViewModel binding).</summary>
    public event EventHandler? ProfileChanged;

    /// <summary>Fires when the profile was auto-switched (connect/disconnect/startup fallback).</summary>
    public event EventHandler<ProfileSwitchNotification>? AutoSwitched;

    public SourceProfileManager(
        IEnumerable<ISourceProfile> profiles,
        CameraManager camera,
        AudioInputManager mic,
        AudioOutputManager speaker,
        ISettingsService settings,
        ILogger<SourceProfileManager> log)
    {
        _profiles = profiles.OrderBy(p => p.Order).ToList().AsReadOnly();
        _camera = camera;
        _mic = mic;
        _speaker = speaker;
        _settings = settings;
        _log = log;

        // Set _activeProfile from persisted settings (no apply yet — just in-memory)
        var savedId = _settings.DeviceSettings.ActiveProfileId;
        _activeProfile = _profiles.FirstOrDefault(p => p.Id == savedId);
    }

    /// <summary>Currently active source profile, or null if none.</summary>
    public ISourceProfile? ActiveProfile => _activeProfile;

    /// <summary>All registered profiles, ordered by <see cref="ISourceProfile.Order"/>.</summary>
    public IReadOnlyList<ISourceProfile> AvailableProfiles => _profiles;

    /// <summary>
    /// Switch to the profile with the given ID and apply its device configuration.
    /// Persists the choice to settings. Marks the switch as user-initiated.
    /// </summary>
    public async Task ApplyProfileAsync(string profileId, CancellationToken ct = default)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            _log.LogWarning("Profile '{ProfileId}' not found; ignoring", profileId);
            return;
        }

        _log.LogInformation("Applying source profile '{ProfileId}' ({DisplayName})",
            profile.Id, profile.DisplayName);

        await profile.ApplyAsync(_camera, _mic, _speaker, ct);

        _activeProfile = profile;
        _lastSwitchWasManual = true;

        // Persist
        var ds = _settings.DeviceSettings;
        ds.ActiveProfileId = profile.Id;
        _settings.DeviceSettings = ds;

        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called when a device connects. Auto-upgrades to a higher-priority profile
    /// if one just became available, unless the user explicitly chose Custom.
    /// </summary>
    public async Task HandleDeviceConnectedAsync(CancellationToken ct = default)
    {
        // Never auto-switch away from Custom — user chose it explicitly
        if (_activeProfile?.Id == "custom")
        {
            _log.LogDebug("Custom profile active; skipping auto-upgrade on device connect");
            return;
        }

        var currentPriority = _activeProfile?.FallbackPriority ?? -1;

        // If user manually selected the current profile and it's still available,
        // only upgrade if the new profile has strictly higher priority
        var candidate = _profiles
            .Where(p => p.IsAvailable && p.FallbackPriority > currentPriority && p.Id != "custom")
            .OrderByDescending(p => p.FallbackPriority)
            .FirstOrDefault();

        if (candidate is null)
        {
            _log.LogDebug("No higher-priority profile available after device connect");
            return;
        }

        var oldName = _activeProfile?.DisplayName ?? "(none)";
        _log.LogInformation("Auto-upgrading from '{OldProfile}' to '{NewProfile}' (device connected)",
            _activeProfile?.Id ?? "(none)", candidate.Id);

        await SwitchAutoAsync(candidate, new ProfileSwitchNotification
        {
            OldProfileName = oldName,
            NewProfileName = candidate.DisplayName,
            Reason = ProfileSwitchReason.DeviceConnected,
        }, ct);
    }

    /// <summary>
    /// Called when a device disconnects. Falls back to the highest-priority
    /// available profile if the current one is no longer available.
    /// </summary>
    public async Task HandleDeviceDisconnectedAsync(CancellationToken ct = default)
    {
        // If current profile is still available, keep it
        if (_activeProfile is not null && _activeProfile.IsAvailable)
        {
            _log.LogDebug("Current profile '{ProfileId}' still available after disconnect", _activeProfile.Id);
            return;
        }

        var oldName = _activeProfile?.DisplayName ?? "(none)";
        var fallback = _profiles
            .Where(p => p.IsAvailable)
            .OrderByDescending(p => p.FallbackPriority)
            .FirstOrDefault();

        if (fallback is null)
        {
            _log.LogWarning("No available profile found during disconnect fallback");
            return;
        }

        _log.LogInformation("Falling back from '{OldProfile}' to '{NewProfile}' (device disconnected)",
            _activeProfile?.Id ?? "(none)", fallback.Id);

        await SwitchAutoAsync(fallback, new ProfileSwitchNotification
        {
            OldProfileName = oldName,
            NewProfileName = fallback.DisplayName,
            Reason = ProfileSwitchReason.DeviceDisconnected,
        }, ct);
    }

    /// <summary>
    /// Smart fallback: walk profiles by <see cref="ISourceProfile.FallbackPriority"/> descending,
    /// pick the first available one, and apply it. Backward-compatible entry point.
    /// </summary>
    public async Task HandleDeviceChangedAsync(CancellationToken ct = default)
    {
        // If current profile is still available, try to upgrade
        if (_activeProfile is not null && _activeProfile.IsAvailable)
        {
            await HandleDeviceConnectedAsync(ct);
            return;
        }

        // Current profile unavailable — fall back
        await HandleDeviceDisconnectedAsync(ct);
    }

    /// <summary>
    /// Restore the persisted profile on startup. If the saved profile is unavailable,
    /// falls back via priority chain and fires a notification.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var savedId = _settings.DeviceSettings.ActiveProfileId;
        var saved = _profiles.FirstOrDefault(p => p.Id == savedId);

        if (saved is not null && saved.IsAvailable)
        {
            await ApplyProfileAsync(saved.Id, ct);
            _lastSwitchWasManual = false; // startup restore is not a manual choice
        }
        else
        {
            _log.LogInformation("Saved profile '{ProfileId}' unavailable; running startup fallback", savedId);

            var fallback = _profiles
                .Where(p => p.IsAvailable)
                .OrderByDescending(p => p.FallbackPriority)
                .FirstOrDefault();

            if (fallback is not null)
            {
                var oldName = saved?.DisplayName ?? savedId;
                await SwitchAutoAsync(fallback, new ProfileSwitchNotification
                {
                    OldProfileName = oldName,
                    NewProfileName = fallback.DisplayName,
                    Reason = ProfileSwitchReason.StartupFallback,
                }, ct);
            }
        }
    }

    private async Task SwitchAutoAsync(ISourceProfile target,
        ProfileSwitchNotification notification, CancellationToken ct)
    {
        await target.ApplyAsync(_camera, _mic, _speaker, ct);

        _activeProfile = target;
        _lastSwitchWasManual = false;

        var ds = _settings.DeviceSettings;
        ds.ActiveProfileId = target.Id;
        _settings.DeviceSettings = ds;

        ProfileChanged?.Invoke(this, EventArgs.Empty);
        AutoSwitched?.Invoke(this, notification);
    }
}
