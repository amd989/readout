using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WindowHandler = Microsoft.Maui.Platforms.Linux.Gtk4.Handlers.WindowHandler;

namespace Readout.Linux;

/// <summary>
/// The navigation bar Shell has on other platforms, as a GTK header bar in the window's title
/// bar: a back button while a page is pushed (Connect, a session, the graph, About), the page's
/// title, and its toolbar items (a session's Rename). The GTK backend's Shell (preview 13) is
/// only a notebook of tabs, so without this a pushed page had no way back and no toolbar.
/// It also keeps the selected tab in step with Shell: route navigation to another tab
/// ("View" after saving a session) changed the page but left the old tab selected.
/// </summary>
internal static class ShellHeaderBar
{
    private static readonly ConditionalWeakTable<Gtk.Window, Bar> Bars = [];

    // The window's mappings run before the backend shows it, which is when GTK takes a title bar.
    public static void Register() =>
        WindowHandler.Mapper.AppendToMapping("ReadoutHeaderBar", (handler, window) =>
        {
            if (handler.PlatformView is not { } gtkWindow || window is not Window mauiWindow || Bars.TryGetValue(gtkWindow, out _))
                return;
            Bars.Add(gtkWindow, new Bar(gtkWindow, mauiWindow));
        });

    private sealed class Bar
    {
        private readonly Window _window;
        private readonly Gtk.Button _back;
        private readonly Gtk.Label _title;
        private readonly Gtk.Box _actions;
        private Shell? _shell;
        private Page? _page;

        public Bar(Gtk.Window gtkWindow, Window window)
        {
            _window = window;

            var header = Gtk.HeaderBar.New();
            _back = Gtk.Button.NewFromIconName("go-previous-symbolic");
            _back.SetTooltipText("Back");
            _back.SetVisible(false);
            _back.OnClicked += (_, _) => GoBack();
            header.PackStart(_back);

            _title = Gtk.Label.New(window.Title ?? "");
            _title.AddCssClass("title");
            _title.SetEllipsize(Pango.EllipsizeMode.End);
            header.SetTitleWidget(_title);

            _actions = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
            header.PackEnd(_actions);
            gtkWindow.SetTitlebar(header);

            // Escape and Alt+Left go back too, as in GNOME's apps.
            var keys = Gtk.EventControllerKey.New();
            keys.OnKeyPressed += (_, args) =>
            {
                var back = args.Keyval == Gdk.Constants.KEY_Escape ||
                    (args.Keyval == Gdk.Constants.KEY_Left && args.State.HasFlag(Gdk.ModifierType.AltMask));
                if (back && _back.GetVisible()) GoBack();
                return back;
            };
            gtkWindow.AddController(keys);

            window.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Window.Page)) Watch(window.Page as Shell); };
            Watch(window.Page as Shell);
        }

        private void Watch(Shell? shell)
        {
            if (_shell == shell) return;
            if (_shell is not null) _shell.Navigated -= OnNavigated;
            _shell = shell;
            if (shell is not null) shell.Navigated += OnNavigated;
            Update();
        }

        private void OnNavigated(object? sender, ShellNavigatedEventArgs e) => Update();

        private void GoBack()
        {
            if (_shell is { } shell && shell.Navigation.NavigationStack.Count > 1)
                _ = shell.Navigation.PopAsync();
        }

        private void Update()
        {
            var page = _shell?.CurrentPage;
            if (page != _page)
            {
                if (_page is not null)
                {
                    _page.PropertyChanged -= OnPageChanged;
                    if (_page.ToolbarItems is INotifyCollectionChanged old) old.CollectionChanged -= OnToolbarChanged;
                }
                _page = page;
                if (page is not null)
                {
                    page.PropertyChanged += OnPageChanged;
                    if (page.ToolbarItems is INotifyCollectionChanged items) items.CollectionChanged += OnToolbarChanged;
                }
            }

            _back.SetVisible(_shell?.Navigation.NavigationStack.Count > 1);
            UpdateTitle();
            UpdateActions();
            SelectTab();
        }

        private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Page.Title)) UpdateTitle();
        }

        private void OnToolbarChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateActions();

        // The page's title where Shell would show its bar; the app's name where the page hides it (Meter).
        private void UpdateTitle() =>
            _title.SetText(_page is { } page && Shell.GetNavBarIsVisible(page) && !string.IsNullOrEmpty(page.Title)
                ? page.Title
                : _window.Title ?? "");

        private void UpdateActions()
        {
            while (_actions.GetFirstChild() is { } child) _actions.Remove(child);
            if (_page is null) return;
            foreach (var item in _page.ToolbarItems)
            {
                var button = Gtk.Button.NewWithLabel(item.Text ?? "");
                button.SetSensitive(item.IsEnabled);
                button.OnClicked += (_, _) => ((IMenuItemController)item).Activate();
                _actions.Append(button);
            }
        }

        private void SelectTab()
        {
            if (_shell?.CurrentItem is not { } item || item.CurrentItem is null ||
                _shell.Handler?.PlatformView is not Gtk.Widget root || Find<Gtk.Notebook>(root) is not { } tabs)
                return;
            var index = item.Items.IndexOf(item.CurrentItem);
            if (index >= 0 && index < tabs.GetNPages() && tabs.GetCurrentPage() != index)
                tabs.SetCurrentPage(index);
        }

        private static T? Find<T>(Gtk.Widget widget) where T : Gtk.Widget
        {
            if (widget is T found) return found;
            for (var child = widget.GetFirstChild(); child is not null; child = child.GetNextSibling())
                if (Find<T>(child) is { } inner) return inner;
            return null;
        }
    }
}
