namespace DtHub.Core.Android;

/// <summary>
/// Ce qu'il faut faire d'un texte avant de le confier au shell du téléphone.
///
/// ADB ne transmet pas les arguments un par un : il les recolle par des espaces
/// et laisse le shell de l'appareil les redécouper. Un nom qui contient une
/// espace arrive donc en deux morceaux, et seul le premier compte. Mesuré :
/// <c>pm create-user Compte 3</c> a créé un profil nommé « Compte ».
/// </summary>
public static class AndroidShell
{
    /// <summary>
    /// Le texte tel que le shell de l'appareil le rendra entier.
    ///
    /// Guillemets simples, qui suspendent toute interprétation. Une apostrophe
    /// à l'intérieur ferme la citation : on la referme, on en glisse une
    /// échappée, on rouvre, ce qui est la façon habituelle et la seule sûre.
    /// </summary>
    public static string Quote(string? value)
    {
        var text = value ?? string.Empty;

        return "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
