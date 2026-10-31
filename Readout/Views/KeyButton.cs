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

    private IDispatcherTimer? _timer;
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
        _longSent = false;
        _timer ??= CreateTimer();
        _timer.Start();
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (!_longSent)
        {
            KeyPressed?.Invoke(this, new KeyPressedEventArgs(Key, longPress: false));
            Flash();
        }
    }

    /// <summary>
    /// A quick dip in size when a press is sent: a tap is often over before the Pressed color
    /// shows, so this is what tells you the button took.
    /// </summary>
    private async void Flash()
    {
        await this.ScaleToAsync(0.92, 60, Easing.CubicOut);
        await this.ScaleToAsync(1, 160, Easing.CubicOut);
    }

    private IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = LongPressTime;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _longSent = true;
            Haptic(HapticFeedbackType.LongPress);
            KeyPressed?.Invoke(this, new KeyPressedEventArgs(Key, longPress: true));
            Flash();
        };
        return timer;
    }

    private static void Haptic(HapticFeedbackType type)
    {
        try { HapticFeedback.Default.Perform(type); }
        catch (FeatureNotSupportedException) { }
    }
}
