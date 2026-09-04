namespace VistumblerMAUI.Services;

/// <summary>
/// Where exports are written. Persists the folder the user picked on the Export page,
/// and falls back to the app's own documents folder whenever that pick cannot be used.
///
/// The fallback is not decoration. On Android a folder chosen through the system picker
/// comes back as a Storage Access Framework tree, and the filesystem path the picker
/// reports for it is often not one this app may write to under scoped storage — the
/// failure would otherwise land on the person exporting, as an export that reported
/// success and wrote nothing. <see cref="Resolve"/> proves the folder is writable before
/// anything is exported to it, so a bad pick costs a line of status text rather than the
/// data. The share sheet still runs either way, which is what actually gets a file off a
/// phone.
/// </summary>
public static class ExportLocation
{
    private const string FolderKey = "Export_Folder";

    /// <summary>
    /// The app's own documents folder, used when nothing is chosen and whenever a choice
    /// turns out not to be writable.
    /// </summary>
    /// <remarks>
    /// SpecialFolderOption.Create, because on Android MyDocuments is
    /// /data/user/0/&lt;package&gt;/files/Documents and nothing has made it. The plain
    /// overload returns the path whether or not it exists, so every export on a fresh
    /// install failed with IO_PathNotFound_Path against a directory the app itself was
    /// supposed to own. The extra CreateDirectory is belt and braces: the Create option
    /// is a no-op on platforms where GetFolderPath consults the OS rather than composing
    /// a path, and creating an existing directory costs nothing.
    /// </remarks>
    public static string DefaultFolder
    {
        get
        {
            var folder = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolderOption.Create);
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>The chosen folder, or empty when exports go to <see cref="DefaultFolder"/>.</summary>
    public static string Chosen
    {
        get => Preferences.Get(FolderKey, string.Empty);
        set => Preferences.Set(FolderKey, value?.Trim() ?? string.Empty);
    }

    /// <summary>Forget the chosen folder, so exports go back to <see cref="DefaultFolder"/>.</summary>
    public static void Reset() => Preferences.Remove(FolderKey);

    /// <summary>
    /// The folder to export into, and whether that is the chosen one. Falls back to
    /// <see cref="DefaultFolder"/> when nothing is chosen or the choice cannot be
    /// written to.
    /// </summary>
    public static (string Folder, bool UsedChoice) Resolve()
    {
        var chosen = Chosen;
        if (!string.IsNullOrWhiteSpace(chosen) && IsWritable(chosen))
            return (chosen, true);

        return (DefaultFolder, false);
    }

    /// <summary>
    /// Whether a file can actually be created in <paramref name="folder"/>, established by
    /// creating one and deleting it. Asked before exporting rather than after, so a folder
    /// that cannot be written to does not cost a half-written file.
    /// </summary>
    public static bool IsWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".vistumbler-write-test-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[ExportLocation] '{folder}' is not writable: {ex.Message}");
            return false;
        }
    }
}
