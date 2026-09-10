using System.Globalization;

namespace DtHub.Core.Settings;

/// <summary>
/// Le temps passé sur un compte, jour par jour, sur une semaine glissante.
///
/// Information seulement : aucune limite, aucun rappel, aucun jugement. Qui
/// joue cinq comptes finit par ne plus savoir lequel il fait vraiment tourner,
/// et c'est la seule question à laquelle ce relevé répond.
///
/// **Sept jours, pas plus.** Un cumul depuis toujours ne dit rien d'utile et
/// grossit sans fin dans le fichier de réglages ; une semaine tient en sept
/// entrées et suffit à voir où le temps passe.
///
/// La clef est la date en ISO, « 2026-09-10 ». C'est le seul format qui se
/// trie comme il se lit et qui ne dépende d'aucune culture.
/// </summary>
public static class PlaytimeLog
{
    /// <summary>Jours retenus.</summary>
    public const int Days = 7;

    /// <summary>La clef d'un jour.</summary>
    public static string KeyOf(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Ajoute un temps au jour donné et écarte ce qui sort de la semaine.
    ///
    /// Rend un relevé neuf plutôt que de modifier celui reçu : l'appelant
    /// écrit dans un document de réglages, et une modification en place y
    /// passerait inaperçue.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Add(
        IReadOnlyDictionary<string, int>? existing,
        DateOnly day,
        int seconds)
    {
        Dictionary<string, int> log = existing is null
            ? new(StringComparer.Ordinal)
            : new(existing, StringComparer.Ordinal);

        if (seconds > 0)
        {
            var key = KeyOf(day);

            log[key] = (log.TryGetValue(key, out var already) ? already : 0) + seconds;
        }

        return Trim(log, day);
    }

    /// <summary>
    /// Écarte les jours antérieurs à la semaine qui finit le jour donné, et
    /// ceux dont la clef n'est pas une date.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Trim(
        IReadOnlyDictionary<string, int>? log,
        DateOnly today)
    {
        if (log is null)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var first = today.AddDays(-(Days - 1));

        Dictionary<string, int> kept = new(StringComparer.Ordinal);

        foreach (var (key, seconds) in log)
        {
            // Une clef illisible s'en va : c'est un fichier que quelqu'un peut
            // avoir modifié à la main, et un relevé n'a pas à survivre à sa
            // propre corruption.
            if (seconds > 0 && Parse(key) is { } day && day >= first && day <= today)
            {
                kept[key] = seconds;
            }
        }

        return kept;
    }

    /// <summary>Le total de la semaine qui finit le jour donné, en secondes.</summary>
    public static int Week(IReadOnlyDictionary<string, int>? log, DateOnly today) =>
        Trim(log, today).Values.Sum();

    private static DateOnly? Parse(string key) =>
        DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
}
