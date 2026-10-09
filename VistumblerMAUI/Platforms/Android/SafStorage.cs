#if ANDROID
using Android.App;
using Android.Content;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace VistumblerMAUI.Platforms.Android;

/// <summary>
/// Folders the user picks through Android's Storage Access Framework. Scoped storage doesn't let the app write
/// to most of shared storage through file paths, which is why a folder chosen through a path-based picker came
/// back "can't write there". Instead the folder is picked with ACTION_OPEN_DOCUMENT_TREE, the write permission
/// the user grants is kept across restarts, and files are created and read through the folder's content URI.
/// </summary>
internal static class SafStorage
{
    public const int PickFolderRequestCode = 0x5AF1;
    private static TaskCompletionSource<AndroidUri?>? _pick;

    private static ContentResolver Resolver => global::Android.App.Application.Context.ContentResolver!;

    public static bool IsContentUri(string location) => location.StartsWith("content://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Shows the system folder picker; returns the folder's tree URI with lasting read/write access, or null when cancelled.</summary>
    public static Task<AndroidUri?> PickFolderAsync()
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No activity to show the folder picker from.");
        _pick?.TrySetResult(null);
        _pick = new TaskCompletionSource<AndroidUri?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        activity.StartActivityForResult(intent, PickFolderRequestCode);
        return _pick.Task;
    }

    /// <summary>Called from MainActivity.OnActivityResult.</summary>
    public static void OnPickFolderResult(Result resultCode, Intent? data)
    {
        var pick = _pick;
        _pick = null;
        if (pick is null) return;
        var uri = resultCode == Result.Ok ? data?.Data : null;
        if (uri is not null)
        {
            try
            {
                Resolver.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            }
            catch (Exception ex)
            {
                VistumblerMAUI.Services.DebugLog.Write($"[SafStorage] couldn't keep access to {uri}: {ex.Message}");
            }
        }
        pick.TrySetResult(uri);
    }

    /// <summary>Whether the app still holds write access to a picked folder (the user can revoke it, or the card be removed).</summary>
    public static bool CanWrite(string treeUri) =>
        Resolver.PersistedUriPermissions.Any(p => p.IsWritePermission && p.Uri?.ToString() == treeUri);

    /// <summary>Creates <paramref name="fileName"/> in the folder from a local file and returns the new document's URI.</summary>
    public static string CopyIn(string treeUri, string localFile, string fileName)
    {
        var tree = AndroidUri.Parse(treeUri)!;
        var parent = DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!;
        // application/octet-stream keeps the name exactly as given (a known MIME type could get an extension added)
        var doc = DocumentsContract.CreateDocument(Resolver, parent, "application/octet-stream", fileName)
                  ?? throw new IOException($"Couldn't create {fileName} in {Describe(treeUri)}.");
        using var output = Resolver.OpenOutputStream(doc) ?? throw new IOException($"Couldn't write {fileName}.");
        using var input = File.OpenRead(localFile);
        input.CopyTo(output);
        return doc.ToString()!;
    }

    /// <summary>The files directly in a picked folder (not in its subfolders), as (name, document URI).</summary>
    public static List<(string Name, string Location)> ListFiles(string treeUri)
    {
        var tree = AndroidUri.Parse(treeUri)!;
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!;
        var files = new List<(string, string)>();
        using var cursor = Resolver.Query(children, new[]
        {
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType,
        }, null, null, null);
        while (cursor is not null && cursor.MoveToNext())
        {
            if (cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir) continue;
            var doc = DocumentsContract.BuildDocumentUriUsingTree(tree, cursor.GetString(0))!;
            files.Add((cursor.GetString(1) ?? "", doc.ToString()!));
        }
        return files;
    }

    public static Stream OpenRead(string documentUri) =>
        Resolver.OpenInputStream(AndroidUri.Parse(documentUri)!) ?? throw new IOException("Couldn't open the saved file.");

    public static bool Exists(string documentUri)
    {
        try
        {
            using var cursor = Resolver.Query(AndroidUri.Parse(documentUri)!,
                new[] { DocumentsContract.Document.ColumnDocumentId }, null, null, null);
            return cursor is not null && cursor.Count > 0;
        }
        catch
        {
            return false;   // deleted, or access revoked
        }
    }

    public static void Delete(string documentUri) => DocumentsContract.DeleteDocument(Resolver, AndroidUri.Parse(documentUri)!);

    /// <summary>The document's file name, e.g. "2026-10-07 14-30-05_AutoSave.VS1".</summary>
    public static string FileName(string documentUri)
    {
        var uri = AndroidUri.Parse(documentUri)!;
        try
        {
            using var cursor = Resolver.Query(uri, new[] { DocumentsContract.Document.ColumnDisplayName }, null, null, null);
            if (cursor is not null && cursor.MoveToFirst() && cursor.GetString(0) is { Length: > 0 } name) return name;
        }
        catch { /* fall back to the id */ }
        return DocumentsContract.GetDocumentId(uri)?.Split('/').Last() ?? "file";
    }

    /// <summary>A readable place for a folder or file, e.g. "Phone storage/Documents/Vistumbler" for "primary:Documents/Vistumbler".</summary>
    public static string Describe(string location)
    {
        var uri = AndroidUri.Parse(location)!;
        string? id = null;
        try
        {
            id = DocumentsContract.IsDocumentUri(global::Android.App.Application.Context, uri)
                ? DocumentsContract.GetDocumentId(uri)
                : DocumentsContract.GetTreeDocumentId(uri);
        }
        catch { /* not a document URI */ }
        if (string.IsNullOrEmpty(id)) return location;

        int colon = id.IndexOf(':');
        var volume = colon < 0 ? "" : id[..colon];
        var path = colon < 0 ? id : id[(colon + 1)..];
        var root = volume == "primary" ? "Phone storage" : string.IsNullOrEmpty(volume) ? "Storage" : $"Storage {volume}";
        return string.IsNullOrEmpty(path) ? root : $"{root}/{path}";
    }
}
#endif
