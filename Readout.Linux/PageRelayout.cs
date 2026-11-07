using System.Runtime.CompilerServices;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;
using ViewHandler = Microsoft.Maui.Handlers.ViewHandler;

namespace Readout.Linux;

/// <summary>
/// Lays a page out again when something on it changes size. The GTK backend (preview 13) lays a
/// page out only when its allocation changes; the layout panels inside a Shell page don't run
/// their own passes, so a view that appeared or changed size kept its first bounds: the meter's
/// battery line, filled in a moment after connecting, was drawn at the page's top left. A pass
/// is queued when the page reports MeasureInvalidated (a view shown or hidden, a label's text)
/// or a view's WidthRequest or HeightRequest changes (which doesn't always reach the page), and
/// when the page gets a new size, at most one per page per main-loop turn. From Wavelength's
/// Linux head.
/// </summary>
internal static class PageRelayout
{
    private static readonly ConditionalWeakTable<Page, Pass> Passes = [];

    public static void Register()
    {
        PageHandler.Mapper.AppendToMapping("ReadoutRelayout", (handler, view) =>
        {
            if (view is Page page && !Passes.TryGetValue(page, out _))
            {
                var pass = new Pass(page);
                Passes.Add(page, pass);
                page.MeasureInvalidated += (_, _) => pass.Queue();
                // A change before the page had a size (a font that loaded early) has to wait for it.
                page.SizeChanged += (_, _) => pass.Queue();
            }
        });
        ViewHandler.ViewMapper.AppendToMapping(nameof(IView.Width), (_, view) => Request(view));
        ViewHandler.ViewMapper.AppendToMapping(nameof(IView.Height), (_, view) => Request(view));
    }

    private static void Request(IView view)
    {
        for (var element = view as Element; element is not null; element = element.Parent)
            if (element is Page page && Passes.TryGetValue(page, out var pass))
            {
                pass.Queue();
                return;
            }
    }

    private sealed class Pass(Page page)
    {
        private bool _queued;
        private bool _laying;

        public void Queue()
        {
            if (_queued || _laying) return;
            _queued = true;
            page.Dispatcher.Dispatch(Run);
        }

        private void Run()
        {
            _queued = false;
            var (width, height) = (page.Width, page.Height);
            if (width <= 0 || height <= 0) return;
            _laying = true;
            try
            {
                // Not every change clears its ancestors' measure caches on the way up, so
                // clear them all: these pages are small.
                InvalidateTree(page);
                // The page's own Measure asks GTK for the native size and leaves its content
                // alone; its cross-platform layout measures and arranges the content.
                if (page is not ICrossPlatformLayout layout) return;
                layout.CrossPlatformMeasure(width, height);
                layout.CrossPlatformArrange(new Rect(0, 0, width, height));
            }
            finally { _laying = false; }
        }

        private static void InvalidateTree(IVisualTreeElement element)
        {
            foreach (var child in element.GetVisualChildren())
                InvalidateTree(child);
            (element as IView)?.InvalidateMeasure();
        }
    }
}
