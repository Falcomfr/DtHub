using System.Globalization;
using System.IO;

using DtHub.Core.Diagnostics;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.App.Services;

/// <summary>
/// Rassemble de quoi raconter un incident, et le met en forme.
///
/// Rien n'est envoyé d'ici : le rapport se copie dans le presse-papiers, et
/// c'est la personne qui décide de le coller quelque part. C'est la ligne que
/// suit déjà le signalement vers papycha.fr, où l'application ne remplit que le
/// repère de lieu et n'appuie jamais sur le bouton d'envoi.
///
/// Le lanceur est posé après coup plutôt qu'injecté : il connaît les sessions
/// ouvertes, mais il dépend lui-même de la moitié de l'application, et
/// l'injecter ici fermerait une boucle. C'est la même façon de faire que
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

    /// <summary>Ce que l'application sait des sessions ouvertes, quand elle le sait.</summary>
    public GameLauncher? Launcher { get; set; }

    /// <summary>Le dernier refus technique relevé, s'il y en a eu un.</summary>
    public string? LastFailure { get; set; }

    /// <summary>
    /// Les noms que la personne a choisis : ses comptes, ses profils de
    /// lancement. Ils ne se devinent par aucun motif, et rien n'empêche
    /// quelqu'un d'y mettre son pseudonyme de jeu.
    ///
    /// Ils sont posés du dehors plutôt que lus ici : le panneau reçoit déjà les
    /// réglages à chaque écriture, et un rapport n'a pas à attendre une lecture
    /// de fichier.
    /// </summary>
    public IReadOnlyList<string> Names { get; set; } = [];

    /// <summary>Combien d'incidents ont été relevés depuis le démarrage.</summary>
    public int Incidents { get; private set; }

    /// <summary>Signalé quand un incident vient d'être relevé.</summary>
    public event EventHandler? IncidentRecorded;

    /// <summary>
    /// Retient une faute qui n'a pas été montrée.
    ///
    /// Deux gestionnaires de l'application journalisaient sans rien afficher :
    /// celui du domaine et celui des tâches non observées. Or c'est par là que
    /// passent les fautes hors du fil d'interface, c'est-à-dire ADB, scrcpy et
    /// le réseau. Deux cent soixante-quatre d'entre elles ont été relevées dans
    /// les journaux sans qu'aucune n'ait jamais paru à l'écran.
    ///
    /// On n'ouvre pas une boîte pour autant : une faute de ce genre se répète,
    /// et la boîte deviendrait le vrai problème. Elle est retenue, comptée, et
    /// le panneau la signale d'une ligne qu'on peut ignorer.
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

    /// <summary>Compose le rapport d'un incident.</summary>
    /// <param name="headline">Ce qui a échoué, en une ligne.</param>
    /// <param name="error">L'exception, s'il y en a une.</param>
    public string Compose(string? headline, Exception? error = null) =>
        DiagnosticReport.Compose(headline, Facts(), error, LastFailure, Log(), Secrets());

    /// <summary>L'adresse d'un signalement neuf sur le dépôt.</summary>
    public static string IssueUrl(string? headline) =>
        DiagnosticReport.IssueUrl(Core.ProductInfo.RepositoryUrl, headline);

    private DiagnosticFacts Facts()
    {
        var sessions = Launcher?.ActiveSessions.Count ?? 0;
        var tabbed = sessions - (Launcher?.ManagedSessions.Count ?? 0);

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
    /// Les écrans, dans la même forme que celle déjà journalisée à chaque
    /// lancement. Leur nom de périphérique Windows n'y entre pas : c'est une
    /// empreinte de machine, et la définition suffit à comprendre un placement.
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
    /// Les appareils ouverts, comptés et non nommés : leur nombre et leur
    /// version d'Android expliquent un incident, leur identité non.
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
    /// Les lignes du journal du jour qui valent la peine, celles de cette
    /// session seulement.
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

            // Partagé : Serilog écrit dans le même fichier au même moment.
            using var stream = new FileStream(
                today, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            return LogDigest.Of(reader.ReadToEnd(), AppSession.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Le journal manque : le rapport vaut quand même par son en-tête et
            // par l'exception. Le dire plutôt que rendre un blanc.
            return "(journal illisible : " + exception.GetType().Name + ")";
        }
    }

    /// <summary>
    /// Ce que l'application sait devoir masquer : les identités de ses
    /// appareils et les noms que la personne a donnés à ses comptes. Les formes
    /// reconnaissables, elles, partent toutes seules.
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
