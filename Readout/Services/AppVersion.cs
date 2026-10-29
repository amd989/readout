namespace Readout.Services;

/// <summary>The version as shown in Settings and About.</summary>
public static class AppVersion
{
    /// <summary>
    /// "1.0 (1)" on Apple and Android. Windows packages carry the build in the version itself
    /// (1.0.26301.0, see publish-windows.ps1) and no build number, so there it's "1.0.26301".
    /// </summary>
    public static string Text
    {
        get
        {
            if (DeviceInfo.Platform != DevicePlatform.WinUI)
                return $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
            var version = AppInfo.Current.Version;
            return version.Build >= 0 ? version.ToString(3) : version.ToString();
        }
    }
}
