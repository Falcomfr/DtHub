namespace DtHub.Core.Settings;

/// <summary>Compromis entre finesse de l'image et charge de la machine.</summary>
public enum StreamQuality
{
    /// <summary>Le plus léger : pour les machines et les téléphones modestes.</summary>
    Low,

    /// <summary>Réglage d'origine.</summary>
    Medium,

    /// <summary>Le plus fin, et le plus exigeant.</summary>
    High,
}

/// <summary>
/// Ce que chaque qualité change, en un seul endroit.
///
/// Le poste le plus lourd n'est pas l'image : c'est l'interrogation du
/// téléphone, qui liste les profils et les paquets installés. La qualité basse
/// allège donc les deux, sans quoi elle ne soulagerait que la moitié du
/// problème.
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

        StreamQuality.High => new QualityProfile(
            MaxFps: 60,
            VideoBitrateKbps: 8000,
            MaximumDisplayHeight: int.MaxValue,
            DevicePoll: TimeSpan.FromSeconds(2),
            InstanceRediscovery: TimeSpan.FromSeconds(15),
            WindowWatch: TimeSpan.FromMilliseconds(500)),

        _ => new QualityProfile(
            MaxFps: 45,
            VideoBitrateKbps: 4000,
            MaximumDisplayHeight: int.MaxValue,
            DevicePoll: TimeSpan.FromSeconds(3),
            InstanceRediscovery: TimeSpan.FromSeconds(30),
            WindowWatch: TimeSpan.FromMilliseconds(500)),
    };
}
