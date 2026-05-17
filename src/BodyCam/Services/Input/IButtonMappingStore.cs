namespace BodyCam.Services.Input;

/// <summary>
/// Persistence layer for button gesture-to-action mappings.
/// Wraps ActionMap with a provider+button+gesture composite key.
/// </summary>
public interface IButtonMappingStore
{
    /// <summary>
    /// Get the current action for the given provider+button+gesture.
    /// Returns ButtonAction.None if no mapping exists.
    /// </summary>
    ButtonAction Get(string providerId, string buttonId, ButtonGesture gesture);

    /// <summary>
    /// Set the action for the given provider+button+gesture.
    /// Persists immediately and takes effect on the next gesture.
    /// </summary>
    void Set(string providerId, string buttonId, ButtonGesture gesture, ButtonAction action);

    /// <summary>
    /// Clear a mapping, reverting to the default action.
    /// </summary>
    void Clear(string providerId, string buttonId, ButtonGesture gesture);

    /// <summary>
    /// Load all mappings from persistent storage.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Save all mappings to persistent storage.
    /// </summary>
    Task SaveAsync();
}
