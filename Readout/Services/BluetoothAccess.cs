using Plugin.BLE.Abstractions.Contracts;

namespace Readout.Services;

/// <summary>Asks for the Bluetooth permission where the OS needs it, and explains what's missing.</summary>
/// <param name="state">
/// The radio's state (Plugin.BLE's, or BlueZ's on Linux), asked for only when needed: on Apple
/// platforms that's what shows the permission prompt. Null when the simulated transport stands
/// in for the radio (simulators).
/// </param>
public sealed class BluetoothAccess(Func<Task<BluetoothState>>? state)
{
    /// <summary>Null when scanning can start; otherwise what the user should do.</summary>
    public async Task<string?> EnsureReadyAsync()
    {
        if (state is null) return null;
#if ANDROID
        var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<NearbyDevices>);
        if (status != PermissionStatus.Granted)
            return "Readout needs the Nearby devices permission to find your meter. You can allow it in Settings.";
#endif
        // The radio reports Unknown until the OS answers: for a moment after launch, and for as
        // long as the Bluetooth permission prompt is up (a new Mac build asks again). Wait that
        // out rather than give up, which would leave auto-connect not trying at all.
        var current = await state();
        for (var waited = 0; waited < 30_000 && current is BluetoothState.Unknown or BluetoothState.TurningOn; waited += 100)
        {
            await Task.Delay(100);
            current = await state();
        }

        return current switch
        {
            BluetoothState.On => null,
            BluetoothState.Unauthorized => "Readout isn't allowed to use Bluetooth. You can allow it in Settings.",
#if LINUX
            BluetoothState.Unavailable => "No Bluetooth adapter found. Readout needs BlueZ running and an adapter with Bluetooth Low Energy.",
#else
            BluetoothState.Unavailable => "This device doesn't support Bluetooth Low Energy.",
#endif
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
