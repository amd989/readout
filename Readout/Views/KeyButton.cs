using Readout.Core.Protocol;

namespace Readout.Views;

public sealed class KeyPressedEventArgs(MeterKey key, bool longPress) : EventArgs
{
    public MeterKey Key { get; } = key;
    public bool LongPress { get; } = longPress;
}

/// <summary>
/// One of the meter's buttons. A tap sends a short press; holding it for
/// <see cref="LongPressTime"/> sends a long press (as on the meter itself), once.
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
    }

    public MeterKey Key
    {
        get => (MeterKey)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public event EventHandler<KeyPressedEventArgs>? KeyPressed;

    private void OnPressed(object? sender, EventArgs e)
    {
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

    private IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = LongPressTime;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _longSent = true;
            try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); }
            catch (FeatureNotSupportedException) { }
            KeyPressed?.Invoke(this, new KeyPressedEventArgs(Key, longPress: true));
        };
        return timer;
    }
}
