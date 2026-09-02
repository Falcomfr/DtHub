using DtHub.Core.Storage;

namespace DtHub.Core.Settings;

/// <summary>Compromis entre finesse de l'image et charge de la machine.</summary>
///
/// Un palier retiré du code retombe sur le suivant vers le haut, jamais vers
/// le bas : personne ne doit voir son image se dégrader sans l'avoir demandé.
[JsonFallback(Maximum)]
public enum StreamQuality
{
    /// <summary>Le plus léger : pour les machines et les téléphones modestes.</summary>
    Low,

    /// <summary>Réglage d'origine.</summary>
    Medium,

    /// <summary>
    /// Sans ménagement pour le téléphone : définition sans borne, débit et
    /// images par seconde au maximum. Demande un appareil récent et, en
    /// Wi-Fi, un réseau qui suit.
    ///
    /// Trois paliers, pas quatre : entre deux voisins trop proches, personne
    /// ne sait lequel choisir, et l'écart ne se voit pas.
    /// </summary>
    Maximum,

    /// <summary>
    /// Les valeurs choisies à la main.
    ///
    /// Ce n'est pas un quatrième barreau de l'échelle, et cela ne contredit
    /// donc pas la règle des trois paliers énoncée juste au-dessus : c'est une
    /// sortie de route. Les trois paliers restent le chemin ordinaire, celui
    /// où l'on n'a rien à savoir ; celui-ci sert à qui sait déjà ce qu'il veut.
    ///
    /// Ajouté en dernier, et à ne jamais réordonner : d'anciens fichiers de
    /// réglages portent encore ces valeurs sous forme de nombre.
    /// </summary>
    Custom,
}

/// <summary>
/// Les quatre valeurs du palier personnalisé.
///
/// Le DPI n'en fait pas partie : il se règle déjà sous le nom de « distance
/// dans le jeu », qui dit ce qu'il fait au joueur plutôt que ce qu'il est. Le
/// proposer ici en plus ferait deux réglages pour une seule chose.
/// </summary>
public sealed record CustomQuality
{
    /// <summary>Valeurs de départ : celles du palier moyen, qui est l'origine.</summary>
    public static readonly CustomQuality Default = new();

    /// <summary>Plafond de hauteur de l'afficheur, comme pour les paliers.</summary>
    public int MaximumDisplayHeight { get; init; } = 1080;

    public int MaxFps { get; init; } = 60;

    public int BitrateKbps { get; init; } = 12000;

    /// <summary>
    /// H.264 ou H.265, et rien d'autre.
    ///
    /// Ce ne sont pas les seuls codecs que scrcpy accepte, mais ce sont les
    /// seuls que le téléphone de référence encode en matériel : relevé par
    /// <c>scrcpy --list-encoders</c>, AV1 et VP8 n'y ont qu'un encodeur
    /// logiciel. Les proposer serait un piège, l'encodage logiciel à soixante
    /// images par seconde coûtant bien plus qu'il ne rend.
    /// </summary>
    public string VideoCodec { get; init; } = "h264";

    /// <summary>
    /// Rend une copie aux valeurs tenables. Le fichier de réglages se modifie
    /// à la main : on corrige plutôt que de refuser de démarrer.
    /// </summary>
    public CustomQuality Sanitized() => this with
    {
        MaximumDisplayHeight = Math.Clamp(MaximumDisplayHeight, 240, 7680),
        MaxFps = Math.Clamp(MaxFps, 1, 240),
        BitrateKbps = Math.Clamp(BitrateKbps, QualityProfile.FloorKbps, 100_000),
        VideoCodec = string.Equals(VideoCodec?.Trim(), "h265", StringComparison.OrdinalIgnoreCase)
            ? "h265"
            : "h264",
    };
}

/// <summary>
/// Ce que chaque qualité change, en un seul endroit.
///
/// Le poste le plus lourd n'est pas l'image : c'est l'interrogation du
/// téléphone, qui liste les profils et les paquets installés. La qualité basse
/// allège donc les deux, sans quoi elle ne soulagerait que la moitié du
/// problème.
///
/// Les trois paliers bornent la définition, et pas seulement le plus bas. Sans
/// borne de définition, moyenne et maximale auraient été indiscernables.
///
/// <para>
/// Le débit ne se fixe pas par palier : il se calcule. Un débit fixe par palier
/// donnait l'échelle à l'envers, mesurée en bits par pixel et par image, ce
/// qu'un encodeur reçoit vraiment :
/// </para>
///
/// <code>
/// Basse     1280x720  à  30 ips,  2500 kb/s  ->  0,090 bpp
/// Moyenne   1920x1080 à  60 ips,  6000 kb/s  ->  0,048 bpp
/// Maximale  3840x2160 à 120 ips, 16000 kb/s  ->  0,016 bpp
/// </code>
///
/// <para>
/// La définition et la cadence étaient multipliées par quinze du bas en haut,
/// le débit par six seulement : le palier « maximale » recevait cinq fois et
/// demie moins de bits par pixel que le palier « basse », et rendait donc une
/// image plus grossière en mouvement. C'est l'inverse de ce qu'il promet.
/// </para>
///
/// <para>
/// Le débit suit maintenant la définition et la cadence réellement retenues,
/// à raison de tant de bits par pixel et par image. La référence pour du H.264
/// de bonne facture tourne autour de 0,10 bpp à toute définition : YouTube
/// demande 12 Mb/s en 1080p60, 24 en 1440p60 et 53 en 2160p60, soit 0,096,
/// 0,108 et 0,106.
/// </para>
/// </summary>
public sealed record QualityProfile(
    int MaxFps,
    double BitsPerPixel,
    int CeilingKbps,
    int MaximumDisplayHeight,
    TimeSpan DevicePoll,
    TimeSpan InstanceRediscovery,
    TimeSpan WindowWatch,
    int? FixedKbps = null)
{
    /// <summary>
    /// Plancher de débit. Sous ce seuil, une petite fenêtre rendrait une bouillie
    /// que personne ne veut, et l'économie ne se sentirait sur rien.
    /// </summary>
    public const int FloorKbps = 1500;

    /// <summary>
    /// Débit à demander pour une définition donnée, en kb/s.
    ///
    /// Le plafond compte autant que le calcul, et pour une raison qui ne se
    /// devine pas depuis le code : deux comptes ouverts, ce sont deux flux sur
    /// la même liaison. Un téléphone en Wi-Fi 2,4 GHz annonce cent quarante-
    /// quatre mégabits de lien brut, dont on tire la moitié en pratique.
    /// Demander cinquante mégabits par session ne donnerait pas une image
    /// magnifique, mais des pertes et des saccades.
    /// </summary>
    public int BitrateFor(int width, int height)
    {
        // Un débit choisi à la main est rendu tel quel, sans passer par le
        // plafond : ce plafond protège des paliers automatiques, et celui qui
        // saisit un nombre a déjà tranché. Le verdict en bits par pixel, lui,
        // lui dira ce que ce nombre vaut.
        if (FixedKbps is { } chosen)
        {
            return Math.Clamp(chosen, FloorKbps, 100_000);
        }

        if (width <= 0 || height <= 0)
        {
            return Math.Clamp(FloorKbps, FloorKbps, CeilingKbps);
        }

        var wanted = BitsPerPixel * width * height * MaxFps / 1000.0;

        return Math.Clamp((int)Math.Round(wanted), FloorKbps, CeilingKbps);
    }

    /// <summary>
    /// Réglages d'une qualité donnée.
    ///
    /// <paramref name="custom"/> n'est lu que pour <see cref="StreamQuality.Custom"/>,
    /// et vaut alors ses valeurs d'origine s'il manque : un fichier de réglages
    /// qui annonce le palier personnalisé sans en porter les valeurs doit rendre
    /// une session qui s'ouvre, pas une exception.
    /// </summary>
    public static QualityProfile For(StreamQuality quality, CustomQuality? custom = null) => quality switch
    {
        StreamQuality.Custom => Personalised(custom ?? CustomQuality.Default),

        // Le palier léger l'est par la définition et la cadence, non par une
        // image dégradée : brider les bits par pixel donnerait du flou sans
        // soulager ni le téléphone ni le poste.
        StreamQuality.Low => new QualityProfile(
            MaxFps: 30,
            BitsPerPixel: 0.08,
            CeilingKbps: 4000,
            MaximumDisplayHeight: 720,
            DevicePoll: TimeSpan.FromSeconds(6),
            InstanceRediscovery: TimeSpan.FromSeconds(60),
            WindowWatch: TimeSpan.FromSeconds(1)),

        // Soixante images et non cent vingt : mesuré sur le jeu, il en rend
        // trente-huit. Les cent vingt ne servaient qu'à diviser par deux les
        // bits accordés à chaque image qui existe vraiment.
        StreamQuality.Maximum => new QualityProfile(
            MaxFps: 60,
            BitsPerPixel: 0.11,
            CeilingKbps: 25000,
            MaximumDisplayHeight: int.MaxValue,
            DevicePoll: TimeSpan.FromSeconds(2),
            InstanceRediscovery: TimeSpan.FromSeconds(15),
            WindowWatch: TimeSpan.FromMilliseconds(500)),

        _ => new QualityProfile(
            MaxFps: 60,
            BitsPerPixel: 0.09,
            CeilingKbps: 12000,
            MaximumDisplayHeight: 1080,
            DevicePoll: TimeSpan.FromSeconds(3),
            InstanceRediscovery: TimeSpan.FromSeconds(30),
            WindowWatch: TimeSpan.FromMilliseconds(500)),
    };

    /// <summary>
    /// Le profil bâti sur des valeurs choisies à la main.
    ///
    /// Les cadences de sondage sont celles du palier moyen : elles pèsent sur
    /// le téléphone, non sur l'image, et ne regardent donc pas celui qui règle
    /// sa vidéo. Les bits par pixel n'y servent à rien, le débit étant donné.
    /// </summary>
    private static QualityProfile Personalised(CustomQuality custom)
    {
        var wanted = custom.Sanitized();

        return For(StreamQuality.Medium) with
        {
            MaxFps = wanted.MaxFps,
            MaximumDisplayHeight = wanted.MaximumDisplayHeight,
            FixedKbps = wanted.BitrateKbps,
        };
    }
}
