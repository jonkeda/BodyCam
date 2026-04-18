namespace BodyCam.Helpers;

internal static class MotionPreference
{
    public static bool PrefersReducedMotion
    {
        get
        {
#if WINDOWS
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            return !uiSettings.AnimationsEnabled;
#elif ANDROID
            var resolver = Android.App.Application.Context.ContentResolver;
            var scale = Android.Provider.Settings.Global.GetFloat(
                resolver!, Android.Provider.Settings.Global.AnimatorDurationScale, 1f);
            return scale == 0f;
#elif IOS
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
            return false;
#endif
        }
    }
}
