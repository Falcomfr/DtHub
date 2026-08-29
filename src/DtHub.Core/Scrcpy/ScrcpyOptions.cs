using System.Globalization;

namespace DtHub.Core.Scrcpy;

/// <summary>Mode clavier transmis à scrcpy.</summary>
public enum ScrcpyKeyboardMode
{
    /// <summary>Injection par l'API Android. Fonctionne sans réglage sur le téléphone.</summary>
    Sdk,

    /// <summary>Clavier physique simulé. Meilleure fidélité, demande Android 11 et plus.</summary>
    Uhid,
}

/// <summary>
/// Réglages d'une session scrcpy. Les valeurs par défaut visent un rendu
/// fluide sans saturer le réseau ni l'encodeur du téléphone quand plusieurs
/// sessions tournent en même temps.
/// </summary>
public sealed record ScrcpyOptions
{
    public static readonly ScrcpyOptions Default = new();

    /// <summary>Images par seconde. Au-delà, plusieurs sessions saturent l'encodeur.</summary>
    public int MaxFps { get; init; } = 45;

    /// <summary>Débit vidéo en kilobits par seconde.</summary>
    public int VideoBitrateKbps { get; init; } = 4000;

    /// <summary>
    /// Audio désactivé par défaut : plusieurs sessions simultanées produiraient
    /// un mélange inaudible, et l'audio coûte de la bande passante.
    /// </summary>
    public bool AudioEnabled { get; init; }

    /// <summary>Synchronisation du presse-papiers dans les deux sens.</summary>
    public bool ClipboardSyncEnabled { get; init; } = true;

    /// <summary>
    /// Ouvrir chaque session sur son propre afficheur virtuel. C'est ce qui
    /// permet d'avoir plusieurs applications côte à côte sans qu'elles se
    /// disputent l'écran du téléphone.
    /// </summary>
    public bool UseVirtualDisplay { get; init; } = true;

    /// <summary>
    /// Largeur de l'afficheur virtuel, en pixels. Paysage par défaut : le jeu
    /// s'affiche ainsi, et un afficheur vertical le réduirait à une bande.
    /// </summary>
    public int VirtualDisplayWidth { get; init; } = 1920;

    public int VirtualDisplayHeight { get; init; } = 1080;

    /// <summary>Densité de l'afficheur virtuel. Trop basse, l'interface Android devient minuscule.</summary>
    public int VirtualDisplayDpi { get; init; } = 240;

    /// <summary>
    /// Partir d'un afficheur vide plutôt que du lanceur de l'appareil : c'est
    /// nous qui ouvrons l'application voulue, sur le bon profil.
    /// </summary>
    public bool DisableVirtualDisplayDecorations { get; init; } = true;

    /// <summary>
    /// Redimensionner l'afficheur virtuel en continu pour suivre la fenêtre.
    ///
    /// Activé. C'est ce qui donne une image qui remplit toujours la fenêtre,
    /// et surtout un jeu qui se remet en page : une fenêtre large montre
    /// davantage, elle n'affiche pas la même image étirée.
    ///
    /// Mesuré sur un Xiaomi 13T, en faisant varier la largeur seule : de 1,04
    /// à 2,82 de rapport, aucune bande, et la mise en page suit. Le seul
    /// défaut observé demande un saut brutal des deux dimensions à la fois,
    /// que la souris ne produit pas, et il se corrige au redimensionnement
    /// suivant.
    ///
    /// Contraindre la fenêtre à un rapport fixe avait été essayé : cela
    /// supprimait les bandes, mais en interdisant les formes qui fonctionnent,
    /// donc en privant d'une vue plus large.
    /// </summary>
    public bool FlexDisplay { get; init; } = true;

    /// <summary>
    /// Rappel ajouté au titre de chaque fenêtre de jeu, entre parenthèses.
    /// Les fenêtres se ressemblent et se superposent : le joueur doit pouvoir
    /// lire au-dessus de l'image comment passer à la suivante.
    /// </summary>
    public string? WindowTitleHint { get; init; }

    /// <summary>
    /// Dossier où scrcpy va chercher l'icône de ses fenêtres. Elles portent
    /// sinon celle de scrcpy, qui n'a rien à voir avec l'application. Le
    /// dossier doit contenir un <c>scrcpy.png</c>.
    /// </summary>
    public string? IconDirectory { get; init; }

    public ScrcpyKeyboardMode KeyboardMode { get; init; } = ScrcpyKeyboardMode.Sdk;

    /// <summary>Privilégier la saisie de texte à l'injection de codes touches.</summary>
    public bool PreferText { get; init; } = true;

    /// <summary>Empêcher l'écran du téléphone de s'éteindre pendant la session.</summary>
    public bool KeepDeviceAwake { get; init; } = true;

    /// <summary>Codec vidéo, <c>null</c> pour laisser scrcpy décider.</summary>
    public string? VideoCodec { get; init; }

    /// <summary>Débit vidéo au format attendu par scrcpy.</summary>
    public string VideoBitrateArgument =>
        VideoBitrateKbps.ToString(CultureInfo.InvariantCulture) + "K";

    /// <summary>Définition de l'afficheur virtuel au format attendu par scrcpy.</summary>
    public string VirtualDisplayArgument => string.Create(
        CultureInfo.InvariantCulture,
        $"{VirtualDisplayWidth}x{VirtualDisplayHeight}/{VirtualDisplayDpi}");

    /// <summary>
    /// Rend une copie corrigée si des valeurs aberrantes ont été saisies dans
    /// les paramètres. On préfère corriger que refuser de démarrer.
    /// </summary>
    public ScrcpyOptions Sanitized() => this with
    {
        MaxFps = Math.Clamp(MaxFps, 1, 240),
        VideoBitrateKbps = Math.Clamp(VideoBitrateKbps, 200, 100_000),
        VirtualDisplayWidth = Math.Clamp(VirtualDisplayWidth, 240, 7680),
        VirtualDisplayHeight = Math.Clamp(VirtualDisplayHeight, 240, 7680),
        VirtualDisplayDpi = Math.Clamp(VirtualDisplayDpi, 60, 640),
    };
}
