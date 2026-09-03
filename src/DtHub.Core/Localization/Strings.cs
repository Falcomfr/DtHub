using System.Globalization;
using System.Resources;

namespace DtHub.Core.Localization;

/// <summary>
/// Les textes montrés à l'utilisateur, dans la langue courante.
///
/// Les ressources vivent dans ce projet et non dans celui de l'interface :
/// les deux tiers du texte visible sont écrits ici, dans le domaine, et un
/// jeu de ressources logé côté fenêtres leur serait hors d'atteinte.
///
/// La clé manquante se rend telle quelle plutôt que de lever : une étiquette
/// bizarre à l'écran vaut mieux qu'une fenêtre qui ne s'ouvre pas, et
/// <c>StringsResourceTests</c> garantit qu'aucune ne manque.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("DtHub.Core.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>Rend le texte de cette clé dans la langue de l'interface.</summary>
    public static string Get(string key)
        => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>
    /// Rend le texte de cette clé, ses trous remplis. Les nombres et les dates
    /// y prennent le format du pays, qui est un réglage distinct de la langue.
    /// </summary>
    public static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>
    /// Rend le texte de cette clé dans une langue nommée. Sert aux épreuves,
    /// qui doivent pouvoir lire une langue sans changer celle du fil.
    ///
    /// Le nom diffère de <see cref="Get(string)"/> à dessein : en surcharge,
    /// l'analyse exigerait de passer une culture partout, alors que suivre
    /// celle de l'interface est justement ce qu'on veut à l'écran.
    /// </summary>
    public static string GetIn(string key, CultureInfo culture)
        => Manager.GetString(key, culture) ?? key;
}
