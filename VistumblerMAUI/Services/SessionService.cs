using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// File-backed <see cref="ISessionService"/>. Each session is a <c>.db3</c> file named by
/// timestamp (e.g. <c>2026-07-01 16-19-14.db3</c>) under an app-data "Sessions" folder.
/// </summary>
public class SessionService : ISessionService
{
    private readonly string _folder = Path.Combine(FileSystem.AppDataDirectory, "Sessions");

    public string CurrentSessionPath { get; private set; } = string.Empty;

    public SessionService()
    {
        Directory.CreateDirectory(_folder);

        // One-time cleanup of the pre-session flat databases (app is pre-public).
        foreach (var legacy in new[] { "vistumbler.db3", "vistumbler.v2.db3" })
        {
            try
            {
                var p = Path.Combine(FileSystem.AppDataDirectory, legacy);
                if (File.Exists(p)) File.Delete(p);
            }
            catch { /* best-effort */ }
        }
    }

    public void StartNewSession()
        => CurrentSessionPath = Path.Combine(_folder, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.db3");

    public void ResumeSession(string path) => CurrentSessionPath = path;

    public void DiscardCurrent()
    {
        try { if (File.Exists(CurrentSessionPath)) File.Delete(CurrentSessionPath); }
        catch { /* best-effort */ }
    }

    public IReadOnlyList<SessionInfo> ListSessions()
    {
        if (!Directory.Exists(_folder)) return Array.Empty<SessionInfo>();
        return new DirectoryInfo(_folder).GetFiles("*.db3")
            .OrderByDescending(f => f.CreationTime)
            .Select(f => new SessionInfo(f.FullName, Path.GetFileNameWithoutExtension(f.Name), f.CreationTime, f.Length))
            .ToList();
    }

    public void DeleteSession(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
