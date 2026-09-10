namespace DtHub.Core.Devices;

/// <summary>
/// Le téléphone est-il verrouillé, en ce moment.
///
/// **Sert à ne pas crier au loup.** Sur un appareil dont l'afficheur virtuel
/// n'est pas déverrouillé, voir <see cref="VirtualDisplayTrust" />, la fenêtre
/// montre l'écran de verrouillage à la place du jeu. Mais seulement tant que
/// le téléphone est verrouillé : déverrouillé, la même fenêtre montre le jeu,
/// mesuré sur un Mi 9T Pro sous Android 11.
///
/// Le drapeau de l'afficheur dit ce dont l'appareil est capable, celui-ci dit
/// où il en est. L'avertissement n'a de sens que si les deux concordent, sans
/// quoi l'application annonce un défaut devant un jeu qui s'affiche.
/// </summary>
public static class DeviceLock
{
    /// <summary>
    /// Lit <c>dumpsys trust</c>. Rend <c>null</c> quand la réponse ne dit
    /// rien : ne pas savoir n'est pas une raison d'alarmer.
    ///
    /// L'appareil décrit un utilisateur par ligne, et c'est celle de
    /// l'utilisateur courant qui compte : un profil professionnel a sa propre
    /// ligne, sans état de verrouillage.
    /// </summary>
    public static bool? IsLocked(string? dumpsysTrust)
    {
        if (string.IsNullOrWhiteSpace(dumpsysTrust))
        {
            return null;
        }

        bool? fallback = null;

        foreach (var line in dumpsysTrust.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Read(line) is not { } locked)
            {
                continue;
            }

            if (line.Contains("(current)", StringComparison.Ordinal))
            {
                return locked;
            }

            fallback ??= locked;
        }

        return fallback;
    }

    private static bool? Read(string line)
    {
        const string Marker = "deviceLocked=";

        var at = line.IndexOf(Marker, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var value = line[(at + Marker.Length)..];

        return value.StartsWith('1') ? true : value.StartsWith('0') ? false : null;
    }
}
