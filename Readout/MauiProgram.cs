using Readout.Core.Connection;
using Readout.Core.Logging;
using Readout.Services;
using Readout.ViewModels;
using Readout.Views;
using Microsoft.Extensions.Logging;
using Plugin.BLE.Abstractions.Contracts;

namespace Readout;

public static class MauiProgram
{
    private const string PreferredMeterKey = "preferred-meter-id";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Bluetooth: Plugin.BLE, created lazily (reading Current starts the platform's manager,
        // which on Apple platforms is what shows the permission prompt).
        builder.Services.AddSingleton<IBluetoothLE>(_ => Plugin.BLE.CrossBluetoothLE.Current);
        builder.Services.AddSingleton<SimulatedMeterConnection>();
        builder.Services.AddSingleton(_ => new AppSettings(Preferences.Default));
        builder.Services.AddSingleton<IContinuityTone, ContinuityTone>();
        builder.Services.AddSingleton<ContinuityFeedback>();
        var fakeBluetooth = UseFakeBluetooth();
        builder.Services.AddSingleton(sp => new BluetoothAccess(
            fakeBluetooth ? null : sp.GetRequiredService<IBluetoothLE>));

        builder.Services.AddSingleton(sp =>
        {
            var demo = sp.GetRequiredService<SimulatedMeterConnection>();
            // Simulators and emulators have no Bluetooth: there, Debug builds find the demo
            // meter when they scan (also anywhere with READOUT_FAKE_BLE=1).
            IMeterConnection bluetooth = fakeBluetooth
                ? new SimulatedMeterConnection()
                : ActivatorUtilities.CreateInstance<BleMeterConnection>(sp);

            var session = new MeterSession(bluetooth, demo)
            {
                Dispatch = MainThread.BeginInvokeOnMainThread,
                PreferredMeterId = Preferences.Default.Get<string?>(PreferredMeterKey, null),
                AutoReconnect = sp.GetRequiredService<AppSettings>().AutoReconnect,
            };
            session.PreferredMeterChanged += (_, meter) => Preferences.Default.Set(PreferredMeterKey, meter.Id);
            return session;
        });

        // Saved sessions, and the live recording kept on disk until it's saved or cleared.
        builder.Services.AddSingleton(_ => new SessionStore(Path.Combine(FileSystem.AppDataDirectory, "sessions")));
        builder.Services.AddSingleton(sp => new RecordingAutosave(
            sp.GetRequiredService<MeterSession>().Log,
            Path.Combine(FileSystem.AppDataDirectory, "recording.csv")));

        builder.Services.AddSingleton<MeterViewModel>();
        builder.Services.AddTransient<SessionsViewModel>();
        builder.Services.AddTransient<SessionDetailViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<ConnectViewModel>();

        builder.Services.AddSingleton<MeterPage>();
        builder.Services.AddTransient<ConnectPage>();
        builder.Services.AddTransient<AboutPage>();
        builder.Services.AddTransient<SessionsPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<SessionDetailPage>();
        builder.Services.AddTransient<GraphPage>();

#if IOS || MACCATALYST
        // Tint only: the bars keep the system material (Liquid Glass on iOS 26).
        var accent = UIKit.UIColor.FromRGB(0xD9, 0x73, 0x1A);
        UIKit.UITabBar.Appearance.TintColor = accent;
        UIKit.UINavigationBar.Appearance.TintColor = accent;
#endif

#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        return builder.Build();
    }

    private static bool UseFakeBluetooth()
    {
#if DEBUG
        return DeviceInfo.Current.DeviceType == DeviceType.Virtual
            || Environment.GetEnvironmentVariable("READOUT_FAKE_BLE") == "1";
#else
        return false;
#endif
    }
}
