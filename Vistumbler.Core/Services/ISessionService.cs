namespace Vistumbler.Core.Services;

/// <summary>Metadata for one saved capture session (its own database file).</summary>
public record SessionInfo(string Path, string Name, DateTime Created, long SizeBytes);

/// <summary>
/// Manages capture sessions, each backed by its own timestamped database file (as in the
/// original VistumblerMDB, whose DB was named by date/time). The database service reads the
/// current session path; the startup chooser lists/resumes/deletes prior sessions.
/// </summary>
public interface ISessionService
{
    /// <summary>Full path of the database file for the active session.</summary>
    string CurrentSessionPath { get; }

    /// <summary>Begin a new session (a fresh timestamped database path).</summary>
    void StartNewSession();

    /// <summary>Make an existing session file the active one.</summary>
    void ResumeSession(string path);

    /// <summary>Delete the current session's database file (discard on exit).</summary>
    void DiscardCurrent();

    /// <summary>All saved session database files, newest first.</summary>
    IReadOnlyList<SessionInfo> ListSessions();

    /// <summary>Delete a specific session database file.</summary>
    void DeleteSession(string path);
}
