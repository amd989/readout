using Readout.Core.Protocol;

namespace Readout.Views;

public sealed class KeyPressedEventArgs(MeterKey key, bool longPress) : EventArgs
{
    public MeterKey Key { get; } = key;
    public bool LongPress { get; } = longPress;
}

/// <summary>
/// One of the meter's buttons. A tap sends a short press; holding it for
/// <see cref="LongPressTime"/> sends a long press (as on the meter itself), once. Touching it
/// gives a light click, and the long press a stronger one, so it feels like a real key.
/// </summary>
public sealed class KeyButton : Button
{
    public static readonly TimeSpan LongPressTime = TimeSpan.FromMilliseconds(700);

    public static readonly BindableProperty KeyProperty =
        BindableProperty.Create(nameof(Key), typeof(MeterKey), typeof(KeyButton), MeterKey.Mode);

    // Each press gets a number, so a long-press check left over from an earlier press does nothing.
    private int _press;
    private bool _down;
    private bool _longSent;

    public KeyButton()
    {
        Pressed += OnPressed;
        Released += OnReleased;
        SemanticProperties.SetHint(this, "Hold for a long press");
    }

    public MeterKey Key
    {
        get => (MeterKey)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public event EventHandler<KeyPressedEventArgs>? KeyPressed;

    private void OnPressed(object? sender, EventArgs e)
    {
        Haptic(HapticFeedbackType.Click);
        _down = true;
        _longSent = false;
        // A one-off delayed check rather than a reused timer: on Linux the GTK backend's
        // one-shot timer stays "running" after it fires, so starting it again did nothing and
        // every other hold went out as a short press.
        var press = ++_press;
        Dispatcher.DispatchDelayed(LongPressTime, () => SendLongPress(press));
    }

    private void SendLongPress(int press)
    {
        if (press != _press || !_down || _longSent) return;
        _longSent = true;
        Haptic(HapticFeedbackType.LongPress);
        KeyPressed?.Invoke(this, new KeyPressedEventArgs(Key, longPress: true));
    }

    // A release before the long press is a short press. (So is a Released with no Pressed,
    // as a keyboard activation can be.) Once up, the pending long-press check does nothing.
    private void OnReleased(object? sender, EventArgs e)
    {
        var longSent = _down && _longSent;
        _down = false;
        if (!longSent)
            KeyPressed?.Invoke(this, new KeyPressedEventArgs(Key, longPress: false));
    }

    /// <summary>
    /// Drops a press without sending anything. The Linux head calls this when the pointer is let
    /// go off the button, where GTK raises no Released, so the long press doesn't fire by itself.
    /// </summary>
    public void CancelPress()
    {
        _down = false;
    }

    /// <summary>
    /// Plays a press: the Pressed color and a dip in size, held a little longer for a long
    /// press. For every key that reaches the meter, and for keys pressed on the meter itself,
    /// so the screen follows the real buttons. (A tap is often over before the Pressed color
    /// shows on its own, so this is also what tells you a tap took.)
    /// </summary>
    public async void PlayPress(bool longPress)
    {
        VisualStateManager.GoToState(this, "Pressed");
        await this.ScaleToAsync(0.92, 60, Easing.CubicOut);
        await Task.Delay(longPress ? 400 : 90);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled");
        await this.ScaleToAsync(1, 160, Easing.CubicOut);
    }

    private static void Haptic(HapticFeedbackType type)
    {
        try { HapticFeedback.Default.Perform(type); }
        catch (FeatureNotSupportedException) { }
    }
}
