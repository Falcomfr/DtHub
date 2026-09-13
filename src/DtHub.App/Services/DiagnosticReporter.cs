using System.Globalization;
using System.IO;

using DtHub.Core.Diagnostics;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.App.Services;

/// <summary>
/// Gathers what is needed to describe an incident, and formats it.
///
/// Nothing is sent from here: the report is copied to the clipboard,
/// and it is the person who decides where to paste it. This is the
/// same line already followed by reporting to papycha.fr, where the
/// application only fills in the placeholder and never presses the
/// send button.
///
/// The launcher is set afterward rather than injected: it knows the
/// open sessions, but it itself depends on half the application, and
/// injecting it here would close a loop. This is the same approach as
/// <c>GameLauncher.OwnsWindow</c>.
/// </summary>
public sealed class DiagnosticReporter
{
    private readonly IAppPaths _paths;
    private readonly WindowManagerService _windows;

    public DiagnosticReporter(IAppPaths paths, WindowManagerService windows)
    {
        _paths = paths;
        _windows = windows;
    }

    /// <summary>
    /// What the application knows about open sessions, when it
    /// knows it.
    /// </summary>
    public GameLauncher? Launcher { get; set; }

    /// <summary>The last technical refusal noted, if there was one.</summary>
    public string? LastFailure { get; set; }

    /// <summary>
    /// The names the person has chosen: their accounts, their launch
    /// profiles. They cannot be guessed by any pattern, and nothing
    /// stops someone from putting their in-game nickname there.
    ///
    /// They are set from the outside rather than read here: the panel
    /// already receives the settings on every write, and a report
    /// should not have to wait for a file read.
    /// </summary>
    public IReadOnlyList<string> Names { get; set; } = [];

    /// <summary>How many incidents have been recorded since startup.</summary>
    public int Incidents { get; private set; }

    /// <summary>Raised when an incident has just been recorded.</summary>
    public event EventHandler? IncidentRecorded;

    /// <summary>
    /// Records a fault that was not shown.
    ///
    /// Two of the application's handlers used to log without
    /// displaying anything: the domain one and the unobserved tasks
    /// one. And this is where faults outside the interface thread pass
    /// through, meaning ADB, scrcpy and the network. Two hundred and
    /// sixty-four of them were found in the logs without a single one
    /// ever appearing on screen.
    ///
    /// A box is not opened for all that: a fault of this kind repeats
    /// itself, and the box would become the real problem. It is
    /// recorded, counted, and the panel reports it with a line that
    /// can be ignored.
    /// </summary>
    public void Note(Exception? error, string headline)
    {
        Incidents++;

        if (error is not null)
        {
            LastFailure = $"{headline} - {error.GetType().Name} : {error.Message}";
        }

        IncidentRecorded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Composes the report of an incident.</summary>
    /// <param name="headline">What failed, in one line.</param>
    /// <param name="error">The exception, if there is one.</param>
    public string Compose(string? headline, Exception? error = null) =>
        DiagnosticReport.Compose(headline, Facts(), error, LastFailure, Log(), Secrets());

    /// <summary>The address of a new report on the repository.</summary>
    public static string IssueUrl(string? headline) =>
        DiagnosticReport.IssueUrl(Core.ProductInfo.RepositoryUrl, headline);

    private DiagnosticFacts Facts()
    {
        var sessions = Launcher?.ActiveSessions.Count ?? 0;

        // The count of hosted sessions, not the difference with the
        // windows that placements can arrange: the latter also
        // excludes the locked ones, which it therefore passed off as
        // tabbed.
        var tabbed = Launcher?.HousedCount ?? 0;

        return new DiagnosticFacts(
            $"{Core.ProductInfo.Name} {Core.ProductInfo.Version}",
            Environment.OSVersion.VersionString,
            Environment.Version.ToString(),
            $"{CultureInfo.CurrentUICulture.Name} / {CultureInfo.CurrentCulture.Name}",
            Screens(),
            Devices(),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Comptes ouverts : {sessions}, dont {Math.Max(0, tabbed)} en onglets"));
    }

    /// <summary>
    /// The screens, in the same form already logged at every launch.
    /// Their Windows device name has no place here: it is a machine
    /// fingerprint, and the resolution is enough to understand a
    /// placement.
    /// </summary>
    private string Screens()
    {
        List<string> sizes =
        [
            .. _windows.GetMonitors().Select(monitor => string.Create(
                CultureInfo.InvariantCulture,
                $"{monitor.Bounds.Width}x{monitor.Bounds.Height}")),
        ];

        return sizes.Count == 0 ? string.Empty : "Écrans : " + string.Join(" + ", sizes);
    }

    /// <summary>
    /// The open devices, counted and not named: their number and
    /// their Android version explain an incident, their identity
    /// does not.
    /// </summary>
    private string Devices()
    {
        if (Launcher is not { } launcher)
        {
            return string.Empty;
        }

        var devices = launcher.ActiveSessions
            .Select(s => s.Target.DeviceId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return devices == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"Appareils : {devices}");
    }

    /// <summary>
    /// The worthwhile lines from today's log, those of this session
    /// only.
    /// </summary>
    private string Log()
    {
        try
        {
            var today = Directory
                .EnumerateFiles(_paths.LogsDirectory, "dthub-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (today is null)
            {
                return string.Empty;
            }

            // Shared: Serilog writes to the same file at the same time.
            using var stream = new FileStream(
                today, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            return LogDigest.Of(reader.ReadToEnd(), AppSession.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The log is missing: the report still has value through
            // its header and the exception. Say so rather than
            // return a blank.
            return "(journal illisible : " + exception.GetType().Name + ")";
        }
    }

    /// <summary>
    /// What the application knows it must hide: the identities of its
    /// devices and the names the person has given their accounts.
    /// Recognizable patterns, for their part, are redacted on their
    /// own.
    /// </summary>
    private IEnumerable<string> Secrets()
    {
        foreach (var name in Names)
        {
            yield return name;
        }

        if (Launcher is not { } launcher)
        {
            yield break;
        }

        foreach (var session in launcher.ActiveSessions)
        {
            yield return session.Target.DeviceId;
            yield return session.Target.Serial;
            yield return session.DisplayName;
        }
    }
}
