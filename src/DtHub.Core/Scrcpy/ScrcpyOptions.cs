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

    /// <summary>
    /// Hauteur de l'afficheur virtuel, en pixels.
    ///
    /// L'image est mise à l'échelle de la fenêtre : cette définition ne fixe
    /// donc pas la taille de l'affichage, mais sa finesse et l'échelle de
    /// l'interface du jeu. Elle est choisie au lancement par
    /// <see cref="DisplayLadder"/>, d'après la taille de la fenêtre.
    ///
    /// Rien ici ne dépend de l'appareil : l'afficheur virtuel n'a aucun
    /// rapport avec l'écran du téléphone ou de la tablette. Seul l'encodeur
    /// vidéo pose une limite, variable, d'où la définition de repli.
    /// </summary>
    public int VirtualDisplayHeight { get; init; } = 1080;

    /// <summary>Densité de l'afficheur virtuel. Trop basse, l'interface Android devient minuscule.</summary>
    public int VirtualDisplayDpi { get; init; } = 240;

    /// <summary>
    /// Partir d'un afficheur vide plutôt que du lanceur de l'appareil : c'est
    /// nous qui ouvrons l'application voulue, sur le bon profil.
    /// </summary>
    public bool DisableVirtualDisplayDecorations { get; init; } = true;

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

    /// <summary>
    /// Privilégier la saisie de texte à l'injection de codes touches.
    ///
    /// Éteint : cette option avale les modificateurs. Ctrl+V tapait un « v »
    /// dans le champ au lieu de coller, mesuré, et scrcpy la déconseille
    /// lui-même pour les jeux, où elle casse aussi les touches de
    /// déplacement.
    /// </summary>
    public bool PreferText { get; init; }

    /// <summary>
    /// Colle le presse-papiers de Windows en tapant son contenu, plutôt qu'en
    /// demandant à Android de coller le sien.
    ///
    /// Mesuré : le collage ordinaire ne fait rien. scrcpy pose bien le texte
    /// dans le presse-papiers du téléphone, la trace le dit, mais la touche
    /// COLLER qu'il envoie ensuite n'insère rien dans l'application. Taper le
    /// texte contourne le presse-papiers d'Android en entier.
    /// </summary>
    public bool LegacyPaste { get; init; } = true;

    /// <summary>Empêcher l'écran du téléphone de s'éteindre pendant la session.</summary>
    public bool KeepDeviceAwake { get; init; } = true;

    /// <summary>
    /// Éteindre l'écran du téléphone pendant la session.
    ///
    /// L'image continue d'arriver : vérifié sur le téléphone de référence,
    /// <c>mWakefulness</c> passe de <c>Awake</c> à <c>Dozing</c> et le jeu
    /// s'affiche entièrement dans la fenêtre.
    ///
    /// Le gain est modeste et il faut le dire : mesuré, la dalle s'éteint de
    /// toute façon pendant la session, <see cref="KeepDeviceAwake"/> ne la
    /// retenant pas. Cette option ne fait que l'éteindre <b>tout de suite</b>
    /// au lieu d'attendre le délai de veille, ce qui compte sur un téléphone
    /// réglé pour rester allumé longtemps, ou branché.
    /// </summary>
    public bool TurnScreenOff { get; init; }

    /// <summary>Codec vidéo, <c>null</c> pour laisser scrcpy décider.</summary>
    public string? VideoCodec { get; init; }

    /// <summary>
    /// Codecs que scrcpy 4.1 accepte. Un nom hors de cette liste le fait sortir
    /// aussitôt, et le refus arrive sous une forme que rien ne sait traduire :
    /// l'utilisateur reçoit alors le message générique après le délai complet,
    /// pour une simple faute de frappe dans le fichier de réglages.
    ///
    /// La liste n'est pas une restriction de notre part : c'est celle de
    /// « scrcpy --help ». Que l'appareil sache encoder dans le codec demandé
    /// reste une autre question, et celle-là se lit dans la sortie.
    /// </summary>
    private static readonly string[] KnownCodecs = ["h264", "h265", "av1", "vp8", "vp9"];

    /// <summary>Débit vidéo au format attendu par scrcpy.</summary>
    public string VideoBitrateArgument =>
        VideoBitrateKbps.ToString(CultureInfo.InvariantCulture) + "K";

    /// <summary>Densité la plus basse acceptée.</summary>
    public const int MinDisplayDpi = 60;

    /// <summary>
    /// Densité la plus haute acceptée.
    ///
    /// Ce n'est pas une limite d'Android : mesuré sur le téléphone de
    /// référence, l'afficheur virtuel accepte 640, 800 et même 1200. C'est une
    /// prudence, et elle doit valoir la même partout. Elle valait 800 au calcul
    /// et 640 ici, si bien que toute densité au-dessus de 640 était rabotée en
    /// silence et que le zoom le plus proche saturait bien avant la hauteur
    /// annoncée.
    /// </summary>
    public const int MaxDisplayDpi = 800;

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
        VirtualDisplayDpi = Math.Clamp(VirtualDisplayDpi, MinDisplayDpi, MaxDisplayDpi),
        VideoCodec = SanitizedCodec(),
    };

    /// <summary>
    /// Nom du codec s'il est connu de scrcpy, <c>null</c> sinon : mieux vaut
    /// laisser scrcpy choisir que le faire échouer.
    /// </summary>
    private string? SanitizedCodec()
    {
        if (string.IsNullOrWhiteSpace(VideoCodec))
        {
            return null;
        }

        var value = VideoCodec.Trim().ToLowerInvariant();

        return KnownCodecs.Contains(value, StringComparer.Ordinal) ? value : null;
    }
}
