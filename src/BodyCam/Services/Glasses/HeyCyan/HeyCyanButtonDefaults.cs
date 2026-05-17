namespace BodyCam.Services.Glasses.HeyCyan;

using BodyCam.Services.Input;

/// <summary>
/// Default button-action mappings for the HeyCyan glasses button.
/// </summary>
public static class HeyCyanButtonDefaults
{
    public const string ProviderId = HeyCyanButtonProvider.ProviderIdConst;
    public const string ButtonId   = HeyCyanButtonProvider.ButtonIdConst;

    /// <summary>
    /// Seed default mappings for the HeyCyan glasses button. Existing
    /// user overrides (already present in the ActionMap store) are NOT
    /// overwritten — only unset entries are populated.
    /// </summary>
    public static void SeedDefaults(ActionMap map)
    {
        map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.SingleTap,
                       ButtonAction.ToggleConversation);
        map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.DoubleTap,
                       ButtonAction.Photo);
        map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.LongPress,
                       ButtonAction.EndSession);
    }

    /// <summary>
    /// The fixed set of gestures the HeyCyan firmware can produce.
    /// Used by the settings UI (Wave 3) to render exactly three rows.
    /// </summary>
    public static IReadOnlyList<ButtonGesture> SupportedGestures { get; } =
        new[] { ButtonGesture.SingleTap, ButtonGesture.DoubleTap, ButtonGesture.LongPress };
}
