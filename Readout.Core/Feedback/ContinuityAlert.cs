using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Core.Feedback;

/// <summary>
/// Decides when the phone should beep for continuity, like the meter's own beeper: on below
/// <see cref="OnBelowOhms"/>, off above <see cref="OffAboveOhms"/>, unchanged in between so a
/// reading that wavers at the edge doesn't chatter.
/// </summary>
public sealed class ContinuityAlert
{
    public double OnBelowOhms { get; set; } = 30;
    public double OffAboveOhms { get; set; } = 50;

    public bool IsOn { get; private set; }

    /// <summary>Raised when <see cref="IsOn"/> changes.</summary>
    public event EventHandler<bool>? Changed;

    /// <summary>Only continuity and resistance readings can turn it on; anything else turns it off.</summary>
    public void Update(MeterReading reading)
    {
        if (reading.Mode is not (MeterMode.Continuity or MeterMode.Resistance)
            || reading.BaseValue is not { } ohms || ohms < 0)
        {
            Set(false);
            return;
        }

        if (ohms < OnBelowOhms) Set(true);
        else if (ohms > Math.Max(OffAboveOhms, OnBelowOhms)) Set(false);
    }

    /// <summary>Disconnected, or the feature was turned off.</summary>
    public void Reset() => Set(false);

    private void Set(bool on)
    {
        if (IsOn == on) return;
        IsOn = on;
        Changed?.Invoke(this, on);
    }
}
