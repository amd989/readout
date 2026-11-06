using System.Runtime.CompilerServices;
using Readout.Views;
using ButtonHandler = Microsoft.Maui.Platforms.Linux.Gtk4.Handlers.ButtonHandler;

namespace Readout.Linux;

/// <summary>
/// Pressed for the meter's keys, so holding one sends a long press. The GTK backend's Button
/// (preview 13) raises Clicked and Released on a click but never Pressed, so the hold timer in
/// <see cref="KeyButton"/> never started. A GTK button is in the active state while it's held
/// down: becoming active raises Pressed; losing it without a click (let go off the button)
/// cancels the press, as GTK raises nothing then.
/// </summary>
internal static class KeyButtonPress
{
    private static readonly ConditionalWeakTable<Gtk.Button, object> Attached = [];

    public static void Register() =>
        ButtonHandler.Mapper.AppendToMapping("ReadoutKeyPress", (handler, view) =>
        {
            if (view is not KeyButton key || handler.PlatformView is not { } button || Attached.TryGetValue(button, out _))
                return;
            Attached.Add(button, new object());

            button.OnStateFlagsChanged += (_, args) =>
            {
                var was = args.Flags.HasFlag(Gtk.StateFlags.Active);
                var now = button.GetStateFlags().HasFlag(Gtk.StateFlags.Active);
                if (now && !was)
                    ((IButton)key).Pressed();
                else if (was && !now)
                    // A release on the button clicks, which raises Released and closes the press
                    // before this runs; let go elsewhere, nothing does. Checked a moment later,
                    // when the button has settled.
                    GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 50, () =>
                    {
                        if (!button.GetStateFlags().HasFlag(Gtk.StateFlags.Active)) key.CancelPress();
                        return false;
                    });
            };
        });
}
