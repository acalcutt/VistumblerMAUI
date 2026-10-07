using CommunityToolkit.Maui.Storage;
#if ANDROID
using VistumblerMAUI.Platforms.Android;
#endif

namespace VistumblerMAUI.Services;

/// <summary>
/// Where Save &amp; Clear and Export write files: a folder path, or on Android a folder the user picked through
/// the system picker, which is kept as a content:// URI and written through Android's document APIs
/// (<c>SafStorage</c>) because scoped storage doesn't allow writing there by path. Saved files are likewise a path
/// or a content URI ("location"); the helpers here hide which.
/// </summary>
public static class SaveFolder
{
    public static bool IsContentUri(string location) =>
        location.StartsWith("content://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Shows the folder picker. Returns the chosen folder, or an error explaining why it can't be used; both null when cancelled.</summary>
    public static async Task<(string? Folder, string? Error)> PickAsync(string current)
    {
        try
        {
#if ANDROID
            var uri = await SafStorage.PickFolderAsync();
            if (uri is null) return (null, null);
            var folder = uri.ToString()!;
            return SafStorage.CanWrite(folder)
                ? (folder, null)
                : (null, $"Android didn't give the app access to write to {SafStorage.Describe(folder)}.");
#else
            var result = await FolderPicker.Default.PickAsync(IsContentUri(current) ? "" : current, CancellationToken.None);
            if (!result.IsSuccessful)
                return result.Exception is null or OperationCanceledException
                    ? (null, null)
                    : (null, $"Could not choose a folder: {result.Exception.Message}");
            var picked = result.Folder.Path;
            return ExportLocation.IsWritable(picked) ? (picked, null) : (null, $"Cannot write to {picked}.");
#endif
        }
        catch (Exception ex)
        {
            return (null, $"Could not choose a folder: {ex.Message}");
        }
    }

    /// <summary>Whether files can be saved to the folder now.</summary>
    public static bool IsUsable(string folder)
    {
#if ANDROID
        if (IsContentUri(folder)) return SafStorage.CanWrite(folder);
#endif
        return !IsContentUri(folder) && ExportLocation.IsWritable(folder);
    }

    /// <summary>A readable name for a folder or saved file.</summary>
    public static string Describe(string location)
    {
#if ANDROID
        if (IsContentUri(location)) return SafStorage.Describe(location);
#endif
        return location;
    }

    /// <summary>
    /// Saves <paramref name="fileName"/> into <paramref name="folder"/>: <paramref name="write"/> writes it to the
    /// path it is given. Returns the saved file's location, or null when <paramref name="write"/> wrote nothing.
    /// </summary>
    public static async Task<string?> SaveAsync(string folder, string fileName, Func<string, Task> write)
    {
        if (!IsContentUri(folder))
        {
            var path = Path.Combine(folder, fileName);
            await write(path);
            return File.Exists(path) ? path : null;
        }
#if ANDROID
        // Written locally first, since the exporters write to a path, then copied into the picked folder
        var temp = Path.Combine(FileSystem.CacheDirectory, "saving", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        try
        {
            await write(temp);
            return File.Exists(temp) ? await Task.Run(() => SafStorage.CopyIn(folder, temp, fileName)) : null;
        }
        finally
        {
            try { File.Delete(temp); } catch { /* cache */ }
        }
#else
        throw new NotSupportedException("content:// folders are Android only.");
#endif
    }

    public static string FileName(string location)
    {
#if ANDROID
        if (IsContentUri(location)) return SafStorage.FileName(location);
#endif
        return Path.GetFileName(location);
    }

    public static bool Exists(string location)
    {
#if ANDROID
        if (IsContentUri(location)) return SafStorage.Exists(location);
#endif
        return File.Exists(location);
    }

    public static void Delete(string location)
    {
#if ANDROID
        if (IsContentUri(location)) { SafStorage.Delete(location); return; }
#endif
        File.Delete(location);
    }

    /// <summary>
    /// A file path for a saved file, for uploading or sharing (which need a real file). A content URI is copied
    /// into the cache under its own name; <c>IsCopy</c> says the caller should delete it when done.
    /// </summary>
    public static async Task<(string Path, bool IsCopy)> GetLocalFileAsync(string location)
    {
        if (!IsContentUri(location)) return (location, false);
#if ANDROID
        var copy = Path.Combine(FileSystem.CacheDirectory, "shared", SafStorage.FileName(location));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        await Task.Run(() =>
        {
            using var input = SafStorage.OpenRead(location);
            using var output = File.Create(copy);
            input.CopyTo(output);
        });
        return (copy, true);
#else
        throw new NotSupportedException("content:// files are Android only.");
#endif
    }
}
