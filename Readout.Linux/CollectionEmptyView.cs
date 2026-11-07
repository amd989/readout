using System.Runtime.CompilerServices;
using Microsoft.Maui.Platform;
using CollectionViewHandler = Microsoft.Maui.Platforms.Linux.Gtk4.Handlers.CollectionViewHandler;

namespace Readout.Linux;

/// <summary>
/// Shows a CollectionView's EmptyView when it's a view. The GTK backend (preview 13) only does
/// text: it puts <c>EmptyView.ToString()</c> in a label, so the empty Sessions list read
/// "Microsoft.Maui.Controls.VerticalStackLayout" and the scan list "Microsoft.Maui.Controls.Label".
/// The backend swaps its scrolled window's child between the list and that label; when the
/// label goes in, the view replaces it, and is laid out to the list's size while it shows
/// (nothing in the backend measures or arranges it there).
/// </summary>
internal static class CollectionEmptyView
{
    private static readonly ConditionalWeakTable<Gtk.ScrolledWindow, Shown> Lists = [];

    public static void Register() =>
        CollectionViewHandler.Mapper.AppendToMapping("ReadoutEmptyView", (handler, view) =>
        {
            if (view is not CollectionView list || handler.PlatformView is not { } scroller) return;
            if (!Lists.TryGetValue(scroller, out var shown))
            {
                shown = new Shown(handler, list, scroller);
                Lists.Add(scroller, shown);
                scroller.OnNotify += (_, args) =>
                {
                    if (args.Pspec.GetName() == "child") shown.Update();
                };
            }
            shown.Update();
        });

    private sealed class Shown(CollectionViewHandler handler, CollectionView list, Gtk.ScrolledWindow scroller)
    {
        // A ContentView around the empty view, so there's one widget to swap in.
        private readonly ContentView _host = new();
        private Gtk.Widget? _widget;
        private uint _tick;
        private (int Width, int Height) _laidOut;

        public void Update()
        {
            // Only in place of the backend's text label, and only for a view (text it shows itself).
            if (list.EmptyView is not View empty || handler.MauiContext is not { } context) return;
            if (Child() is not Gtk.Label) return;

            if (_host.Parent is null) list.AddLogicalChild(_host); // for its styles and resources
            if (_host.Content != empty) _host.Content = empty;
            if (_host.ToPlatform(context) is not Gtk.Widget widget) return;
            _widget = widget;
            widget.Unparent();
            widget.SetVexpand(true);
            widget.SetHexpand(true);
            scroller.SetChild(widget);
            _laidOut = default;
            if (_tick == 0) _tick = scroller.AddTickCallback((_, _) => Layout());
        }

        private Gtk.Widget? Child()
        {
            var child = scroller.GetChild();
            return child is Gtk.Viewport viewport ? viewport.GetChild() : child;
        }

        // Every frame while the empty view shows: lay it out again if the list changed size.
        private bool Layout()
        {
            if (Child() is not { } child || child != _widget)
            {
                _tick = 0;
                return false;
            }

            var size = (scroller.GetWidth(), scroller.GetHeight());
            if (size.Item1 > 0 && size.Item2 > 0 && size != _laidOut)
            {
                _laidOut = size;
                IView host = _host;
                host.Measure(size.Item1, size.Item2);
                host.Arrange(new Rect(0, 0, size.Item1, size.Item2));
            }
            return true;
        }
    }
}
