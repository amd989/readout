namespace Readout.Core.Protocol;

/// <summary>How the 121GW shows up over Bluetooth Low Energy.</summary>
/// <remarks>
/// The meter's radio is a Silicon Labs (Bluegiga) BLE module running its serial "cable
/// replacement" profile: one characteristic that sends indications (the packets) and
/// accepts writes (key presses).
/// </remarks>
public static class MeterBluetooth
{
    /// <summary>The data characteristic: packets arrive here, key presses are written here.</summary>
    public static readonly Guid DataCharacteristicUuid = Guid.Parse("E7ADD780-B042-4876-AAE1-112855353CC1");

    /// <summary>
    /// Standard services every device has (GAP, GATT, device information, battery). When the
    /// data characteristic isn't found by UUID, the search for one skips these.
    /// </summary>
    public static readonly IReadOnlySet<Guid> StandardServices = new HashSet<Guid>
    {
        Guid.Parse("00001800-0000-1000-8000-00805F9B34FB"),
        Guid.Parse("00001801-0000-1000-8000-00805F9B34FB"),
        Guid.Parse("0000180A-0000-1000-8000-00805F9B34FB"),
        Guid.Parse("0000180F-0000-1000-8000-00805F9B34FB"),
    };

    /// <summary>
    /// Whether an advertised name looks like a 121GW. The official app accepted "121GW" and the
    /// module's default name, "Bluegiga".
    /// </summary>
    public static bool IsMeterName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        (name.Contains("121GW", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Bluegiga", StringComparison.OrdinalIgnoreCase));
}
