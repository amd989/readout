namespace Readout.Linux;

/// <summary>
/// GTK's save dialog, for exporting a session. MAUI has no save picker, and on Linux the
/// Essentials Share only opens the file with its default app (xdg-open), so a desktop asks
/// where to put the copy instead.
/// </summary>
internal static class FileSaveDialog
{
    /// <summary>Saves a copy of <paramref name="source"/> where the user picks; false if they cancel.</summary>
    public static async Task<bool> SaveCopyAsync(string source, string suggestedName, string title)
    {
        var dialog = Gtk.FileDialog.New();
        dialog.SetTitle(title);
        dialog.SetInitialName(suggestedName);
        dialog.SetModal(true);
        // Documents (XDG_DOCUMENTS_DIR), else home: GTK would otherwise start wherever the app was run from.
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(folder)) dialog.SetInitialFolder(Gio.FileHelper.NewForPath(folder));

        Gio.File? file;
        try
        {
            file = await dialog.SaveAsync(ActiveWindow());
        }
        catch (GLib.GException)
        {
            return false; // dismissed
        }
        if (file?.GetPath() is not { } path) return false;

        // The dialog has already asked before replacing a file.
        File.Copy(source, path, overwrite: true);
        return true;
    }

    private static Gtk.Window? ActiveWindow() =>
        Gtk.Application.GetDefault() is Gtk.Application app ? app.GetActiveWindow() : null;
}
