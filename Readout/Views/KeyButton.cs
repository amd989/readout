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
            KeyPressed?.Invoke(this, new KeyPressedEventArgs(Key, longPress: false));
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
        };
        return timer;
    }

    private static void Haptic(HapticFeedbackType type)
    {
        try { HapticFeedback.Default.Perform(type); }
        catch (FeatureNotSupportedException) { }
    }
}
