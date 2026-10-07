namespace VistumblerMAUI.Services;

public enum SaveFileFormat { Vs1, Vsz }

public enum AutoSaveTrigger { ApCount, Time }

/// <summary>
/// Settings for Save &amp; Clear (hamburger menu) and Auto Save And Clear, after the original Vistumbler's
/// [AutoSaveAndClear] settings and SaveDirAuto folder: where the file goes, what it is called, and when the
/// automatic save runs. Backed by MAUI <see cref="Preferences"/>.
/// </summary>
public static class SaveAndClearSettings
{
    private const string FolderKey         = "SaveClear_Folder";
    private const string FileNameKey       = "SaveClear_FileName";
    private const string FormatKey         = "SaveClear_Format";
    private const string AutoKey           = "SaveClear_Auto";
    private const string TriggerKey        = "SaveClear_Trigger";
    private const string ApCountKey        = "SaveClear_ApCount";
    private const string MinutesKey        = "SaveClear_Minutes";
    private const string UploadKey         = "SaveClear_UploadToWifiDb";
    private const string DeleteAfterUploadKey = "SaveClear_DeleteAfterUpload";

    public const string DefaultFileName = "AutoSave";

    /// <summary>The chosen folder, or empty when saves go to <see cref="ExportLocation.DefaultFolder"/>.</summary>
    public static string Folder
    {
        get => Preferences.Get(FolderKey, string.Empty);
        set => Preferences.Set(FolderKey, value?.Trim() ?? string.Empty);
    }

    public static void ResetFolder() => Preferences.Remove(FolderKey);

    /// <summary>
    /// The folder to save into, and whether that is the chosen one. Like exports, falls back to the app's
    /// documents folder when nothing is chosen or the choice can no longer be written to.
    /// </summary>
    public static (string Folder, bool UsedChoice) Resolve()
    {
        var chosen = Folder;
        if (!string.IsNullOrWhiteSpace(chosen) && SaveFolder.IsUsable(chosen))
            return (chosen, true);
        return (ExportLocation.DefaultFolder, false);
    }

    /// <summary>The name after the timestamp, as in the original's "2026-10-07 14-30-05_AutoSave.VS1".</summary>
    public static string FileName
    {
        get => Preferences.Get(FileNameKey, DefaultFileName);
        set => Preferences.Set(FileNameKey, string.IsNullOrWhiteSpace(value) ? DefaultFileName : value.Trim());
    }

    public static SaveFileFormat Format
    {
        get => (SaveFileFormat)Preferences.Get(FormatKey, (int)SaveFileFormat.Vs1);
        set => Preferences.Set(FormatKey, (int)value);
    }

    /// <summary>Run Save &amp; Clear automatically while scanning.</summary>
    public static bool AutoEnabled
    {
        get => Preferences.Get(AutoKey, false);
        set => Preferences.Set(AutoKey, value);
    }

    public static AutoSaveTrigger Trigger
    {
        get => (AutoSaveTrigger)Preferences.Get(TriggerKey, (int)AutoSaveTrigger.ApCount);
        set => Preferences.Set(TriggerKey, (int)value);
    }

    /// <summary>Auto save once the list holds this many APs. The original defaulted to 1000 because its list slowed down; this one copes with far more.</summary>
    public static int ApCount
    {
        get => Preferences.Get(ApCountKey, 5000);
        set => Preferences.Set(ApCountKey, Math.Max(10, value));
    }

    /// <summary>Auto save after this many minutes of scanning (the original's default is 60).</summary>
    public static int Minutes
    {
        get => Preferences.Get(MinutesKey, 60);
        set => Preferences.Set(MinutesKey, Math.Max(1, value));
    }

    /// <summary>Upload each saved file to WifiDB with the account in Settings → WifiDB.</summary>
    public static bool UploadToWifiDb
    {
        get => Preferences.Get(UploadKey, false);
        set => Preferences.Set(UploadKey, value);
    }

    /// <summary>
    /// Delete a saved file once WifiDB has it (accepted the upload, or already had the same file). Only applies
    /// to files Save &amp; Clear uploads, and only with <see cref="UploadToWifiDb"/> on.
    /// </summary>
    public static bool DeleteAfterUpload
    {
        get => Preferences.Get(DeleteAfterUploadKey, false);
        set => Preferences.Set(DeleteAfterUploadKey, value);
    }

    public static string Extension(SaveFileFormat format) => format == SaveFileFormat.Vsz ? ".VSZ" : ".VS1";

    /// <summary>"yyyy-MM-dd HH-mm-ss_&lt;name&gt;.VS1", with characters a file name can't hold removed.</summary>
    public static string BuildFileName(DateTime localTime)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(FileName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (name.Length == 0) name = DefaultFileName;
        return $"{localTime:yyyy-MM-dd HH-mm-ss}_{name}{Extension(Format)}";
    }
}
