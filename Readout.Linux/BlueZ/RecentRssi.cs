namespace Readout.Linux.BlueZ;

/// <summary>
/// The connected meter's signal strength, while it's recent. BlueZ reports RSSI only from
/// advertisements and has no D-Bus call to read it on a connection, so after connecting the
/// last value from the scan may be all there is. Rather than show it forever, it counts as
/// unknown once <see cref="FreshFor"/> passes without a new one.
/// </summary>
internal sealed class RecentRssi(TimeProvider time)
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(30);

    private readonly Lock _lock = new();
    private int? _value;
    private long _at;

    public int? Value
    {
        get
        {
            lock (_lock)
                return _value is { } v && time.GetElapsedTime(_at) <= FreshFor ? v : null;
        }
    }

    public void Set(int rssi)
    {
        lock (_lock)
        {
            _value = rssi;
            _at = time.GetTimestamp();
        }
    }

    public void Clear()
    {
        lock (_lock) _value = null;
    }
}
