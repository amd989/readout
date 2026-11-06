using System.Runtime.CompilerServices;
using Readout.Views;
using GraphicsViewHandler = Microsoft.Maui.Platforms.Linux.Gtk4.Handlers.GraphicsViewHandler;

namespace Readout.Linux;

/// <summary>
/// Mouse controls for the interactive trend graph. The GTK backend (preview 13) runs a tap
/// recognizer's command but not its Tapped event or its tap count, so double-tap never reset
/// the zoom; its pan looks for a SendPan method MAUI 10 doesn't have, so dragging did nothing;
/// and its pinch reports no origin, so a touchpad pinch zooms around the left edge. Here the
/// wheel (or a touchpad's two-finger scroll) zooms around the pointer, dragging pans, and a
/// double-click shows everything.
/// </summary>
internal static class TrendMouse
{
    private const double StepScale = 1.25;

    private static readonly ConditionalWeakTable<Gtk.Widget, object> Attached = [];

    public static void Register() =>
        GraphicsViewHandler.Mapper.AppendToMapping("ReadoutTrendMouse", (handler, view) =>
        {
            if (view is not TrendView { Interactive: true } trend || handler.PlatformView is not Gtk.Widget widget ||
                Attached.TryGetValue(widget, out _))
                return;
            Attached.Add(widget, new object());

            var pointerX = 0.0;
            var motion = Gtk.EventControllerMotion.New();
            motion.OnMotion += (_, args) => pointerX = args.X;
            widget.AddController(motion);

            var scroll = Gtk.EventControllerScroll.New(Gtk.EventControllerScrollFlags.Vertical);
            scroll.OnScroll += (_, args) =>
            {
                var width = widget.GetWidth();
                if (width <= 0 || args.Dy == 0) return false;
                // Up (negative) zooms in. Touchpads send small fractions, wheels whole steps.
                trend.ZoomAt(Math.Pow(StepScale, -args.Dy), Math.Clamp(pointerX / width, 0, 1));
                return true;
            };
            widget.AddController(scroll);

            var lastX = 0.0;
            var drag = Gtk.GestureDrag.New();
            drag.OnDragBegin += (_, _) => lastX = 0;
            drag.OnDragUpdate += (_, args) =>
            {
                trend.PanBy(args.OffsetX - lastX);
                lastX = args.OffsetX;
            };
            widget.AddController(drag);

            var click = Gtk.GestureClick.New();
            click.OnPressed += (_, args) =>
            {
                if (args.NPress == 2) trend.ResetZoom();
            };
            widget.AddController(click);
        });
}
