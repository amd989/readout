using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using ViewHandler = Microsoft.Maui.Handlers.ViewHandler;

namespace Readout.Linux;

/// <summary>
/// Gives views their gesture recognizers on GTK. The backend (preview 13) has the GTK side,
/// <see cref="GtkGestureExtensions.AttachGestures"/>, but nothing calls it, so tapping a meter in
/// the scan list, a saved session or the trend card did nothing. Attached when a view gets its
/// platform widget, and again when its recognizers change. From Wavelength's Linux head.
/// </summary>
internal static class GestureWiring
{
    private static readonly ConditionalWeakTable<View, object> Watched = [];

    public static void Register() =>
        ViewHandler.ViewMapper.AppendToMapping("ReadoutGestures", (handler, view) =>
        {
            if (view is not View mauiView || handler.PlatformView is not Gtk.Widget widget) return;
            if (mauiView.GestureRecognizers.Count > 0) GtkGestureExtensions.AttachGestures(widget, mauiView);

            if (Watched.TryGetValue(mauiView, out _) ||
                mauiView.GestureRecognizers is not INotifyCollectionChanged changes) return;
            Watched.Add(mauiView, new object());
            changes.CollectionChanged += (_, _) =>
            {
                if (mauiView.Handler?.PlatformView is Gtk.Widget current)
                    GtkGestureExtensions.AttachGestures(current, mauiView);
            };
        });
}
