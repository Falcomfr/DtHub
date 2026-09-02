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
    TimeSpan WindowWatch)
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
        if (width <= 0 || height <= 0)
        {
            return Math.Clamp(FloorKbps, FloorKbps, CeilingKbps);
        }

        var wanted = BitsPerPixel * width * height * MaxFps / 1000.0;

        return Math.Clamp((int)Math.Round(wanted), FloorKbps, CeilingKbps);
    }

    /// <summary>Réglages d'une qualité donnée.</summary>
    public static QualityProfile For(StreamQuality quality) => quality switch
    {
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
}
