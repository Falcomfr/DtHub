namespace DtHub.Core.Localization;

/// <summary>
/// Choisit la langue de l'interface.
///
/// La règle est celle qu'on peut expliquer en une phrase : la langue
/// d'affichage de Windows si on la sert, l'anglais sinon, et un réglage
/// manuel qui l'emporte sur les deux. La comparaison se fait sur les deux
/// premières lettres : « fr-BE » et « es-419 » sont servis comme « fr » et
/// « es », ce qui évite d'énumérer les variantes régionales.
/// </summary>
public static class AppLanguage
{
    /// <summary>La langue embarquée dans l'assembly, celle qui reste quand rien ne correspond.</summary>
    public const string Neutral = "en";

    /// <summary>Les langues traduites, la neutre en tête.</summary>
    public static readonly IReadOnlyList<string> Supported = [Neutral, "fr", "es"];

    /// <summary>
    /// Rend la langue à poser, à partir du réglage de l'application
    /// (vide pour « suivre Windows ») et de la langue d'affichage de Windows.
    /// </summary>
    public static string Choose(string? preferred, string? windows)
        => Match(preferred) ?? Match(windows) ?? Neutral;

    /// <summary>
    /// Vrai s'il faut relancer pour que le choix se voie, c'est-à-dire si la
    /// langue qu'il désigne n'est pas celle qui est déjà affichée.
    ///
    /// Le message d'invitation se levait auparavant à tout changement de
    /// réglage et ne redescendait jamais : revenir à la langue du départ, donc
    /// renoncer, laissait pourtant l'invitation, pour une application qui
    /// n'avait plus rien à changer. Ce qui compte n'est pas qu'on ait touché au
    /// réglage, mais que le choix s'écarte de ce qui est affiché.
    ///
    /// « Suivre Windows » est résolu comme au démarrage : le choisir alors que
    /// Windows parle déjà la langue affichée ne demande donc rien non plus.
    /// </summary>
    /// <param name="preferred">Le réglage choisi, vide pour « suivre Windows ».</param>
    /// <param name="windows">La langue d'affichage de Windows.</param>
    /// <param name="inForce">La langue actuellement affichée.</param>
    public static bool NeedsRestart(string? preferred, string? windows, string? inForce) =>
        !string.Equals(Choose(preferred, windows), Choose(inForce, null), StringComparison.Ordinal);

    /// <summary>Vrai si cette langue est traduite.</summary>
    public static bool Serves(string? culture) => Match(culture) is not null;

    /// <summary>Rend la langue servie pour cette culture, ou rien.</summary>
    private static string? Match(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return null;
        }

        // « fr-BE », « es_MX » : seule la partie qui précède le séparateur
        // nomme la langue.
        var language = culture.Trim();
        var separator = language.IndexOfAny(['-', '_']);

        if (separator >= 0)
        {
            language = language[..separator];
        }

        foreach (var served in Supported)
        {
            if (string.Equals(served, language, StringComparison.OrdinalIgnoreCase))
            {
                return served;
            }
        }

        return null;
    }
}
