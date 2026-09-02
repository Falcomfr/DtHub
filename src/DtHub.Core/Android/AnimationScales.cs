using System.Globalization;

namespace DtHub.Core.Android;

/// <summary>
/// Les trois échelles d'animation d'Android, telles que le téléphone les
/// rapporte.
///
/// Ce sont des réglages <b>globaux</b> de l'appareil, pas des options de
/// session : les écrire laisse une trace qui survit à la fermeture de DT Hub.
/// C'est la raison d'être de ce type, qui existe pour pouvoir rendre au
/// téléphone exactement ce qu'on y a trouvé.
///
/// Une valeur jamais fixée est rendue par Android sous la forme
/// <c>null</c>, en toutes lettres, et vaut alors 1. On la relit donc plutôt
/// que de la supposer.
/// </summary>
public readonly record struct AnimationScales(double Window, double Transition, double Animator)
{
    /// <summary>Ce qu'annonce un téléphone qui n'a jamais été touché.</summary>
    public static readonly AnimationScales Normal = new(1, 1, 1);

    /// <summary>Toutes les animations coupées.</summary>
    public static readonly AnimationScales Off = new(0, 0, 0);

    /// <summary>Les clés Android, dans l'ordre des composantes de ce type.</summary>
    public static readonly string[] Keys =
    [
        "window_animation_scale",
        "transition_animation_scale",
        "animator_duration_scale",
    ];

    /// <summary>Les trois valeurs dans l'ordre de <see cref="Keys"/>.</summary>
    public IReadOnlyList<double> Values => [Window, Transition, Animator];

    /// <summary>Vrai si les trois sont déjà à zéro : rien à faire alors.</summary>
    public bool AllOff => Window == 0 && Transition == 0 && Animator == 0;

    /// <summary>
    /// Lit une valeur rendue par <c>settings get global</c>.
    ///
    /// Rend 1 pour tout ce qui ne se lit pas : une valeur jamais fixée, une
    /// commande refusée, une sortie vide. Se tromper vers 1 rend au téléphone
    /// son comportement d'origine, alors que se tromper vers 0 lui laisserait
    /// les animations coupées sans que personne ne l'ait demandé.
    /// </summary>
    public static double ParseScale(string? output)
    {
        var text = output?.Trim();

        if (string.IsNullOrEmpty(text)
            || text.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && value >= 0
            ? value
            : 1;
    }

    /// <summary>Les trois sorties de <c>settings get</c>, dans l'ordre des clés.</summary>
    public static AnimationScales Parse(string? window, string? transition, string? animator) =>
        new(ParseScale(window), ParseScale(transition), ParseScale(animator));

    /// <summary>La valeur telle qu'elle s'écrit dans une commande ADB.</summary>
    public static string Text(double scale) =>
        scale.ToString("0.0##", CultureInfo.InvariantCulture);
}
