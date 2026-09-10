namespace DtHub.Core.Devices;

/// <summary>Ce que le téléphone fait d'un événement d'entrée qu'on lui envoie.</summary>
public enum InputInjection
{
    /// <summary>On n'a pas su conclure, et on ne conclut donc pas.</summary>
    Unknown = 0,

    /// <summary>L'appareil accepte la simulation d'entrée.</summary>
    Works,

    /// <summary>L'appareil la refuse : l'image passera, les clics non.</summary>
    Denied,
}

/// <summary>
/// Éprouve la seule chose qu'aucun message n'annonce : le droit d'injecter une
/// entrée.
///
/// C'est le symptôme le plus fréquent du terrain et le plus démuni. scrcpy
/// n'échoue pas, aucune erreur ne paraît, la fenêtre s'ouvre et le clic ne fait
/// rien. La cause tient à un réglage des surcouches Xiaomi, Oppo, realme et
/// vivo, « Accorder les autorisations et simulation d'entrée via le débogage
/// USB ». Sans lui, ADB affiche mais n'injecte pas.
///
/// La sonde envoie la touche « inconnue » d'Android, qui ne déclenche rien
/// nulle part : ce n'est pas une commande de jeu, c'est une question posée au
/// système. Elle n'est envoyée que sur demande de l'utilisateur, depuis la
/// fiche d'aide, et jamais pendant une partie.
///
/// Le verdict est prudent par construction. On ne dit « ça marche » que sur un
/// silence complet, et « refusé » que sur un refus nommé. Tout le reste est
/// <see cref="InputInjection.Unknown"/> : se tromper de diagnostic coûterait
/// plus cher que de n'en donner aucun.
/// </summary>
public static class InputInjectionCheck
{
    /// <summary>
    /// Touche « inconnue ». Android l'accepte partout et n'en fait rien : c'est
    /// ce qui permet de poser la question sans agir sur l'appareil.
    /// </summary>
    public const string ProbeKeyCode = "0";

    /// <summary>Commande envoyée au shell de l'appareil.</summary>
    public static IReadOnlyList<string> ProbeCommand { get; } =
        ["shell", "input", "keyevent", ProbeKeyCode];

    /// <summary>
    /// Ce qu'Android écrit quand il refuse. La formulation varie d'une version
    /// et d'une surcouche à l'autre : on cherche donc ce qui ne varie pas.
    /// </summary>
    private static readonly string[] Refusals =
    [
        "inject_events",
        "injecting input",
        "injecting to another application",
        "not allowed to inject",
    ];

    /// <summary>Ce qui dit un refus sans nommer l'injection.</summary>
    private static readonly string[] Denials =
    [
        "securityexception",
        "permission denial",
        "permission denied",
    ];

    /// <summary>Lit le verdict dans ce que la sonde a rendu.</summary>
    /// <param name="exitCode">Code de sortie de la commande.</param>
    /// <param name="standardOutput">Sortie standard.</param>
    /// <param name="standardError">Sortie d'erreur.</param>
    public static InputInjection Read(int exitCode, string? standardOutput, string? standardError)
    {
        var said = ((standardOutput ?? string.Empty) + "\n" + (standardError ?? string.Empty))
            .ToLowerInvariant();

        if (Refusals.Any(r => said.Contains(r, StringComparison.Ordinal)))
        {
            return InputInjection.Denied;
        }

        // Un refus générique ne compte que s'il parle bien d'entrée : le shell
        // rend la même famille d'erreur pour un dossier sécurisé verrouillé,
        // qui n'a rien à voir avec la souris.
        if (Denials.Any(d => said.Contains(d, StringComparison.Ordinal))
            && said.Contains("input", StringComparison.Ordinal))
        {
            return InputInjection.Denied;
        }

        // Le succès est muet : la touche inconnue ne produit aucune sortie.
        // Une commande qui parle sans qu'on sache de quoi ne prouve rien.
        return exitCode == 0 && string.IsNullOrWhiteSpace(said)
            ? InputInjection.Works
            : InputInjection.Unknown;
    }
}
