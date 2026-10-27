using Readout.Core.Connection;
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
            };
            session.PreferredMeterChanged += (_, meter) => Preferences.Default.Set(PreferredMeterKey, meter.Id);
            return session;
        });

        builder.Services.AddSingleton<MeterViewModel>();
        builder.Services.AddTransient<ConnectViewModel>();

        builder.Services.AddSingleton<MeterPage>();
        builder.Services.AddTransient<ConnectPage>();
        builder.Services.AddTransient<AboutPage>();

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
