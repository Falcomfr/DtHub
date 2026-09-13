namespace DtHub.Core.Storage;

/// <summary>
/// User data locations. Everything is grouped under a single folder
/// so a clean uninstall stays simple to explain.
/// </summary>
public interface IAppPaths
{
    /// <summary><c>%LOCALAPPDATA%\&lt;Slug&gt;</c>.</summary>
    string Root { get; }

    string SettingsFile { get; }
    string DevicesFile { get; }
    string ProfilesFile { get; }

    /// <summary>Cached application metadata and icons.</summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Papycha quest catalog. Stored in the cache and not near the
    /// settings: this is not a user choice, and losing it only costs
    /// a reindex.
    /// </summary>
    string QuestCatalogFile { get; }

    /// <summary>Logs with rotation.</summary>
    string LogsDirectory { get; }

    /// <summary>
    /// Downloaded third-party components, one subfolder per version.
    /// </summary>
    string ToolsDirectory { get; }

    /// <summary>
    /// Downloaded updates, waiting to be applied on exit. In the
    /// user's folder and not near the executable: this is the only
    /// place where writing requires no special rights.
    /// </summary>
    string UpdatesDirectory { get; }

    /// <summary>
    /// What the rendering engine writes for itself: its cache, its
    /// cookies, its preferences.
    ///
    /// Without this path, it drops them next to the executable.
    /// Measured on a blank folder, twenty-four megabytes after a
    /// single session; on a development folder a few weeks old,
    /// three hundred ninety-nine. A single file that can be handed
    /// to someone should not leave that behind.
    /// </summary>
    string WebViewDirectory { get; }

    /// <summary>Creates the missing folders. Idempotent.</summary>
    void EnsureCreated();
}
