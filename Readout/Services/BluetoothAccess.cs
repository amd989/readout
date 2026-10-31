using Plugin.BLE.Abstractions.Contracts;

namespace Readout.Services;

/// <summary>Asks for the Bluetooth permission where the OS needs it, and explains what's missing.</summary>
/// <param name="ble">The radio; null when the simulated transport stands in for it (simulators).</param>
public sealed class BluetoothAccess(Func<IBluetoothLE>? ble)
{
    /// <summary>Null when scanning can start; otherwise what the user should do.</summary>
    public async Task<string?> EnsureReadyAsync()
    {
        if (ble is null) return null;
#if ANDROID
        var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<NearbyDevices>);
        if (status != PermissionStatus.Granted)
            return "Readout needs the Nearby devices permission to find your meter. You can allow it in Settings.";
#endif
        // Right after launch the radio reports Unknown until the OS answers. Auto-connect
        // runs then, so give it a moment rather than give up.
        for (var waited = 0; waited < 3000 && ble().State is BluetoothState.Unknown or BluetoothState.TurningOn; waited += 100)
            await Task.Delay(100);

        return ble().State switch
        {
            BluetoothState.On => null,
            BluetoothState.Unauthorized => "Readout isn't allowed to use Bluetooth. You can allow it in Settings.",
            BluetoothState.Unavailable => "This device doesn't support Bluetooth Low Energy.",
            BluetoothState.Off or BluetoothState.TurningOff => "Bluetooth is off. Turn it on to find your meter.",
            _ => "Bluetooth isn't ready yet. Try again in a moment.",
        };
    }
}

#if ANDROID
/// <summary>
/// Android 12+ asks for "Nearby devices" (scan and connect); older versions need location to scan.
/// </summary>
public sealed class NearbyDevices : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? [(Android.Manifest.Permission.BluetoothScan, true), (Android.Manifest.Permission.BluetoothConnect, true)]
            : [(Android.Manifest.Permission.AccessFineLocation, true)];
}
#endif
