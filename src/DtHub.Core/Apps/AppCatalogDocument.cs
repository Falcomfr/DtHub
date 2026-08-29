namespace DtHub.Core.Apps;

/// <summary>
/// Cache disque du catalogue d'applications. Un balayage complet prend
/// plusieurs secondes par profil : sans cache, chaque ouverture de la page
/// Applications serait pénible.
/// </summary>
public sealed class AppCatalogDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Catalogues indexés par identité d'appareil.</summary>
    public Dictionary<string, DeviceCatalog> Devices { get; set; } = [];
}

/// <summary>Catalogue d'un appareil, un jeu d'applications par utilisateur Android.</summary>
public sealed class DeviceCatalog
{
    /// <summary>Clé : identifiant d'utilisateur Android, en texte pour rester lisible en JSON.</summary>
    public Dictionary<string, UserCatalog> Users { get; set; } = [];
}

/// <summary>Applications relevées pour un utilisateur Android.</summary>
public sealed class UserCatalog
{
    public DateTimeOffset UpdatedUtc { get; set; }

    public List<StoredApp> Apps { get; set; } = [];
}

/// <summary>Forme persistée d'une application.</summary>
public sealed class StoredApp
{
    public string PackageName { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? LaunchComponent { get; set; }
    public bool IsSystem { get; set; }

    public static StoredApp From(AndroidApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new StoredApp
        {
            PackageName = app.PackageName,
            Label = app.Label,
            LaunchComponent = app.LaunchComponent,
            IsSystem = app.IsSystem,
        };
    }

    public AndroidApp ToApp(int userId) => new()
    {
        PackageName = PackageName,
        UserId = userId,
        Label = Label,
        LaunchComponent = LaunchComponent,
        IsSystem = IsSystem,
    };
}
