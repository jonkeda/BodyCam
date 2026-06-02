using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;

namespace BodyCam;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TryRunHeyCyanProbe(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        TryRunHeyCyanProbe(intent);
    }

    private static void TryRunHeyCyanProbe(Intent? intent)
    {
        Log.Info("BodyCamHeyCyanProbe", $"MainActivity intent action={intent?.Action ?? "<null>"}");

        if (intent?.Action != Platforms.Android.HeyCyan.HeyCyanAndroidProbe.Action)
            return;

        _ = Platforms.Android.HeyCyan.HeyCyanAndroidProbe.RunFromIntentAsync(intent);
    }
}
