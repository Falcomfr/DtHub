namespace DtHub.Core.Devices;

/// <summary>
/// L'afficheur virtuel créé par scrcpy est-il déverrouillé.
///
/// **Le défaut que ce type existe pour attraper.** Sur un téléphone trop
/// ancien, scrcpy crée bien son afficheur, l'annonce, la fenêtre s'ouvre, et
/// l'application dit « 0 problème ». Mais l'afficheur n'est pas déverrouillé :
/// le système y impose l'écran de verrouillage, et le jeu ne peut pas s'y
/// lancer. Ce qu'on voit est une horloge et un cadenas.
///
/// Mesuré sur deux appareils, mêmes options, même version de scrcpy :
///
/// <code>
/// Mi 9T Pro, Android 11 : FLAG_ROTATES_WITH_CONTENT, FLAG_PRESENTATION,
///                         FLAG_OWN_CONTENT_ONLY
/// 13T Pro,   Android 16 : … FLAG_TRUSTED, FLAG_ALWAYS_UNLOCKED, FLAG_OWN_FOCUS …
/// </code>
///
/// **On mesure au lieu de supposer une version.** Le niveau d'API exact à
/// partir duquel le drapeau apparaît dépend d'Android et du constructeur, et
/// deux appareils ne suffisent pas à le fixer. Le drapeau, lui, se lit : il
/// est là ou il ne l'est pas, sur l'appareil qu'on a devant soi.
/// </summary>
public static class VirtualDisplayTrust
{
    /// <summary>Le drapeau qui décide : sans lui, l'afficheur suit le verrouillage.</summary>
    public const string UnlockedFlag = "FLAG_ALWAYS_UNLOCKED";

    /// <summary>
    /// Vrai si l'afficheur virtuel de scrcpy est déverrouillé, <c>null</c> si
    /// on ne le trouve pas.
    ///
    /// <c>null</c> et non faux : aucun afficheur trouvé veut dire que scrcpy
    /// n'en a pas créé, ou que la sortie a changé de forme. Alarmer sur une
    /// ignorance serait pire que se taire.
    /// </summary>
    public static bool? IsUnlocked(string? dumpsysDisplay)
    {
        if (string.IsNullOrWhiteSpace(dumpsysDisplay))
        {
            return null;
        }

        // La ligne de l'afficheur de scrcpy se reconnaît à son nom, que scrcpy
        // donne lui-même, et à son type virtuel.
        foreach (var line in dumpsysDisplay.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("DisplayDeviceInfo{\"scrcpy\"", StringComparison.Ordinal)
                || (line.Contains("uniqueId=\"virtual:", StringComparison.Ordinal)
                    && line.Contains("scrcpy", StringComparison.OrdinalIgnoreCase)))
            {
                return line.Contains(UnlockedFlag, StringComparison.Ordinal);
            }
        }

        return null;
    }
}
