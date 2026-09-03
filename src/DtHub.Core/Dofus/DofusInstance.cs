namespace DtHub.Core.Dofus;

/// <summary>
/// Une instance du jeu : un téléphone, un profil Android, une installation.
/// Deux instances sur le même téléphone correspondent au profil principal et à
/// un profil cloné, chacune avec son propre compte.
/// </summary>
public sealed record DofusInstance
{
    /// <summary>Identité stable du téléphone, indépendante du mode de connexion.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Nom du téléphone, tel qu'affiché.</summary>
    public required string DeviceName { get; init; }

    /// <summary>Profil Android. N'importe quel entier positif est valide.</summary>
    public required int UserId { get; init; }

    /// <summary>Nom du profil Android tel que le téléphone le rapporte.</summary>
    public required string UserName { get; init; }

    public required string PackageName { get; init; }

    /// <summary>Composant à lancer, résolu à la découverte.</summary>
    public string? LaunchComponent { get; init; }

    /// <summary>Nom choisi par l'utilisateur, affiché dans le titre de la fenêtre.</summary>
    public string? CustomName { get; init; }

    /// <summary>Vrai si l'instance fait partie du lancement automatique.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Vrai si la fenêtre suit les placements automatiques. Décochée, elle
    /// reste où elle est et le reste s'arrange sans elle.
    /// </summary>
    public bool IsManaged { get; init; } = true;

    /// <summary>
    /// Vrai si ce compte s'ouvre dans le cadre à onglets plutôt qu'en fenêtre
    /// libre. Un compte logé échappe aux placements automatiques.
    /// </summary>
    public bool IsTabbed { get; init; }

    /// <summary>Vrai si le téléphone est joignable maintenant.</summary>
    public bool IsDeviceConnected { get; init; }

    /// <summary>
    /// Clé stable de l'instance. Sert à la mémoriser et à la retrouver entre
    /// deux lancements, y compris quand le téléphone change d'adresse.
    /// </summary>
    public string Key => $"{DeviceId}|{UserId}|{PackageName}";

    /// <summary>
    /// Nom modifiable de l'instance. Le choix de l'utilisateur prime ; à
    /// défaut, le nom du profil Android, qui distingue déjà les instances d'un
    /// même téléphone. Le nom du produit n'y figure pas : il est ajouté au
    /// titre de la fenêtre de jeu, pas ici.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(CustomName)
        ? UserName
        : CustomName.Trim();
}
