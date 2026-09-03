using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

using DtHub.Core.Storage;
using DtHub.Core.Updates;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Updates;

/// <summary>
/// Tient l'application à jour depuis les livraisons du dépôt.
///
/// Le fil est le suivant : au démarrage on demande la dernière livraison ; si
/// elle est plus récente et que la mise à jour automatique est cochée, on la
/// télécharge en fond, on vérifie son empreinte, et on la pose à côté. Rien
/// n'est remplacé en pleine session : l'échange se fait à l'arrêt, quand plus
/// rien ne tourne. La note de version, elle, attend le démarrage suivant, celui
/// qui exécute enfin la nouvelle version.
///
/// Aucun échec n'est une panne : pas de réseau, dépôt absent, empreinte fausse,
/// fichier verrouillé, l'application continue avec la version qu'elle a.
/// </summary>
public sealed partial class UpdateService(
    IReleaseSource source,
    IAppPaths paths,
    UpdateTarget target,
    ILogger<UpdateService> logger)
{
    private readonly IReleaseSource _source = source;
    private readonly IAppPaths _paths = paths;
    private readonly UpdateTarget _target = target;
    private readonly ILogger<UpdateService> _logger = logger;

    /// <summary>La version qui tourne.</summary>
    public Version Running { get; } = ReleaseParser.Normalize(target?.Running);

    /// <summary>La livraison plus récente trouvée, s'il y en a une.</summary>
    public AppRelease? Available { get; private set; }

    /// <summary>Vrai quand cette livraison est téléchargée et vérifiée.</summary>
    public bool Ready { get; private set; }

    /// <summary>Signalé quand l'un des deux précédents change.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Cherche une livraison plus récente, et la prépare si on lui a demandé de
    /// le faire tout seul.
    /// </summary>
    public async Task CheckAsync(bool automatic, CancellationToken cancellationToken = default)
    {
        var latest = await _source.GetLatestAsync(cancellationToken).ConfigureAwait(true);

        if (latest is null || latest.Version <= Running)
        {
            return;
        }

        // Rien ne sert de proposer ce qu'on ne pourra pas poser : dans un arbre
        // de sources, le lanceur republie à chaque démarrage et écraserait la
        // mise à jour dans la seconde.
        if (!UpdatePaths.CanReplace(_target.ExecutablePath, File.Exists))
        {
            LogSourceTree(latest.Version.ToString());

            return;
        }

        Available = latest;
        LogFound(latest.Version.ToString(), Running.ToString());
        Changed?.Invoke(this, EventArgs.Empty);

        if (automatic)
        {
            _ = await PrepareAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Télécharge la livraison et vérifie son empreinte. Vrai si elle est prête
    /// à être posée.
    /// </summary>
    public async Task<bool> PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (Available is not { } release)
        {
            return false;
        }

        var staged = UpdatePaths.Staged(_paths.UpdatesDirectory, release.Version);

        try
        {
            _ = Directory.CreateDirectory(_paths.UpdatesDirectory);

            if (!File.Exists(staged))
            {
                await _source
                    .DownloadAsync(release.DownloadUrl, staged, progress: null, cancellationToken)
                    .ConfigureAwait(true);
            }

            var expected = Digest(await _source
                .ReadAsync(release.DigestUrl, cancellationToken)
                .ConfigureAwait(true));

            var actual = await Sha256Async(staged, cancellationToken).ConfigureAwait(true);

            if (expected.Length == 0
                || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                // Un exécutable qui ne correspond pas à ce que le dépôt annonce
                // ne remplace rien du tout, et ne reste pas sur le disque.
                LogDigestMismatch(release.Version.ToString());
                Delete(staged);

                return false;
            }

            await File
                .WriteAllTextAsync(
                    UpdatePaths.Notes(_paths.UpdatesDirectory, release.Version),
                    ReleaseNotes.Readable(release.Notes),
                    cancellationToken)
                .ConfigureAwait(true);

            Ready = true;
            LogReady(release.Version.ToString());
            Changed?.Invoke(this, EventArgs.Empty);

            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            LogPrepareFailed(exception);
            Delete(staged);

            return false;
        }
    }

    /// <summary>
    /// Pose la mise à jour préparée, à l'arrêt. Vrai si l'exécutable a bien été
    /// remplacé.
    ///
    /// Un exécutable qui tourne ne peut pas être écrasé, mais il peut être
    /// renommé : l'ancien s'écarte, le nouveau prend sa place, et le démarrage
    /// suivant balaie ce qui reste. Si la seconde moitié échoue, la première est
    /// défaite : mieux vaut l'ancienne version que pas d'application.
    /// </summary>
    public bool Apply()
    {
        var current = _target.ExecutablePath;

        if (!Ready
            || Available is not { } release
            || string.IsNullOrEmpty(current)
            || !UpdatePaths.CanReplace(current, File.Exists))
        {
            return false;
        }

        var staged = UpdatePaths.Staged(_paths.UpdatesDirectory, release.Version);

        if (!File.Exists(staged))
        {
            return false;
        }

        var retired = Path.Combine(
            Path.GetDirectoryName(current) ?? string.Empty, UpdatePaths.Retired);

        try
        {
            Delete(retired);
            File.Move(current, retired);

            try
            {
                File.Move(staged, current);
            }
            catch (IOException)
            {
                File.Move(retired, current);

                throw;
            }

            LogApplied(release.Version.ToString());

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogApplyFailed(exception);

            return false;
        }
    }

    /// <summary>
    /// Ce que la version qui vient d'être posée annonce, ou une chaîne vide.
    /// Lue une fois : le fichier est effacé au passage, pour que la note ne
    /// revienne pas à chaque démarrage.
    /// </summary>
    public string TakeNotes()
    {
        var file = UpdatePaths.Notes(_paths.UpdatesDirectory, Running);

        try
        {
            if (!File.Exists(file))
            {
                return string.Empty;
            }

            var notes = File.ReadAllText(file);
            Delete(file);

            return notes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Silence assumé : les notes de version sont un agrément. Sans
            // elles la fenêtre « Nouveautés » ne paraît pas, et la mise à jour
            // s'est faite quand même.
            return string.Empty;
        }
    }

    /// <summary>
    /// Balaie ce que la mise à jour précédente a laissé : l'ancien exécutable
    /// écarté, et les livraisons plus anciennes que celle qui tourne.
    /// </summary>
    public void Sweep()
    {
        var directory = Path.GetDirectoryName(_target.ExecutablePath);

        if (directory is not null)
        {
            Delete(Path.Combine(directory, UpdatePaths.Retired));
        }

        try
        {
            if (!Directory.Exists(_paths.UpdatesDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_paths.UpdatesDirectory, "DtHub-*.exe"))
            {
                var version = ReleaseParser.VersionOf(
                    Path.GetFileNameWithoutExtension(file).Replace("DtHub-", string.Empty, StringComparison.Ordinal));

                if (version is null || version <= Running)
                {
                    Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSweepFailed(exception);
        }
    }

    /// <summary>
    /// L'empreinte que porte le fichier d'accompagnement. Le format d'usage est
    /// « empreinte  nom-du-fichier » ; on ne retient que la première.
    /// </summary>
    private static string Digest(string? content)
    {
        var text = (content ?? string.Empty).Trim();
        var cut = text.IndexOfAny([' ', '\t', '\r', '\n']);

        if (cut > 0)
        {
            text = text[..cut];
        }

        return text.Length == 64 && text.All(Uri.IsHexDigit) ? text : string.Empty;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexStringLower(hash);
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Un fichier qu'on n'arrive pas à effacer ne mérite pas d'arrêter
            // quoi que ce soit : il sera repris au ménage suivant.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} disponible, la version en cours est {running}.")]
    private partial void LogFound(string version, string running);

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} ignorée : l'exécutable sort d'un arbre de sources.")]
    private partial void LogSourceTree(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} téléchargée et vérifiée.")]
    private partial void LogReady(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "L'empreinte de la livraison {version} ne correspond pas.")]
    private partial void LogDigestMismatch(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La livraison n'a pas pu être préparée.")]
    private partial void LogPrepareFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Version {version} posée, elle démarrera au prochain lancement.")]
    private partial void LogApplied(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La mise à jour n'a pas pu être posée.")]
    private partial void LogApplyFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le ménage des mises à jour a échoué.")]
    private partial void LogSweepFailed(Exception exception);
}
