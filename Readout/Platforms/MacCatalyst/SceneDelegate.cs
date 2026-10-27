using CoreFoundation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Readout;

/// <summary>The window scene; Info.plist's UIApplicationSceneManifest names it.</summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
    /// <summary>
    /// With a scene, macOS ignores the size MAUI asks for and opens the window at its default
    /// size. So pin the scene to the app's size while macOS places the window, then let the
    /// user resize it down to the minimum.
    /// </summary>
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        base.WillConnect(scene, session, connectionOptions);
        if (scene is not UIWindowScene { SizeRestrictions: { } limits }) return;

        var size = new CGSize(App.WindowSize.Width, App.WindowSize.Height);
        limits.MinimumSize = size;
        limits.MaximumSize = size;
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, TimeSpan.FromSeconds(1)), () =>
        {
            limits.MinimumSize = new CGSize(App.MinimumWindowSize.Width, App.MinimumWindowSize.Height);
            limits.MaximumSize = new CGSize(nfloat.MaxValue, nfloat.MaxValue);
        });
    }
}
