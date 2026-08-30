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
/// Les trois paliers bornent la définition, et pas seulement le plus bas. Le
/// débit et les images par seconde ne se voient pas sur une image presque fixe,
/// l'encodeur n'utilisant que ce dont il a besoin : sans borne de définition,
/// moyenne et haute auraient été indiscernables.
/// </summary>
public sealed record QualityProfile(
    int MaxFps,
    int VideoBitrateKbps,
    int MaximumDisplayHeight,
    TimeSpan DevicePoll,
    TimeSpan InstanceRediscovery,
    TimeSpan WindowWatch)
{
    /// <summary>Réglages d'une qualité donnée.</summary>
    public static QualityProfile For(StreamQuality quality) => quality switch
    {
        StreamQuality.Low => new QualityProfile(
            MaxFps: 30,
            VideoBitrateKbps: 2500,
            MaximumDisplayHeight: 720,
            DevicePoll: TimeSpan.FromSeconds(6),
            InstanceRediscovery: TimeSpan.FromSeconds(60),
            WindowWatch: TimeSpan.FromSeconds(1)),

        StreamQuality.Maximum => new QualityProfile(
            MaxFps: 120,
            VideoBitrateKbps: 16000,
            MaximumDisplayHeight: int.MaxValue,
            DevicePoll: TimeSpan.FromSeconds(2),
            InstanceRediscovery: TimeSpan.FromSeconds(15),
            WindowWatch: TimeSpan.FromMilliseconds(500)),

        _ => new QualityProfile(
            MaxFps: 60,
            VideoBitrateKbps: 6000,
            MaximumDisplayHeight: 1080,
            DevicePoll: TimeSpan.FromSeconds(3),
            InstanceRediscovery: TimeSpan.FromSeconds(30),
            WindowWatch: TimeSpan.FromMilliseconds(500)),
    };
}
