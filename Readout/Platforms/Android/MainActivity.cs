using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.Content;
using Google.Android.Material.AppBar;

namespace Readout;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetStatusBarIcons(Resources?.Configuration);
    }

    /// <summary>
    /// Light/dark switched while the app is open. The activity isn't recreated (UiMode above), so
    /// the title bars already shown keep the old colorPrimary behind the status bar; repaint them
    /// with the new one (values/colors.xml, values-night/colors.xml).
    /// </summary>
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        if (Window?.DecorView is { } decor)
            Repaint(decor, new Android.Graphics.Color(ContextCompat.GetColor(this, Resource.Color.colorPrimary)));
        SetStatusBarIcons(newConfig);
    }

    /// <summary>
    /// The status bar's clock and icons: dark over the white bar of the light theme, light over
    /// the dark one. Set here because the theme doesn't, and a live theme switch doesn't redo it.
    /// </summary>
    private void SetStatusBarIcons(Configuration? config)
    {
        if (config is null || Window is not { DecorView: { } decor } window) return;
        var dark = (config.UiMode & UiMode.NightMask) == UiMode.NightYes;
        if (AndroidX.Core.View.WindowCompat.GetInsetsController(window, decor) is { } insets)
            insets.AppearanceLightStatusBars = !dark;
    }

    private static void Repaint(Android.Views.View view, Android.Graphics.Color color)
    {
        if (view is AppBarLayout bar)
            bar.SetBackgroundColor(color);
        if (view is Android.Views.ViewGroup group)
            for (var i = 0; i < group.ChildCount; i++)
                if (group.GetChildAt(i) is { } child)
                    Repaint(child, color);
    }
}
