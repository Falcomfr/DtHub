namespace DtHub.Core.Profiles;

/// <summary>
/// Un ensemble de sessions à ouvrir ensemble. L'ordre compte : il détermine
/// l'ordre d'empilement des fenêtres et celui du parcours au clavier.
/// </summary>
public sealed record LaunchProfile
{
    /// <summary>Identité interne, stable même si l'utilisateur renomme le profil.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<LaunchTarget> Targets { get; init; } = [];

    public DateTimeOffset? LastUsedUtc { get; init; }

    /// <summary>Nombre de sessions que le profil ouvrira.</summary>
    public int SessionCount => Targets.Count;

    /// <summary>Nombre d'appareils distincts sollicités.</summary>
    public int DeviceCount => Targets.Select(t => t.DeviceId).Distinct(StringComparer.Ordinal).Count();

    /// <summary>Résumé affiché sous le sélecteur de profil.</summary>
    public string Summary => SessionCount switch
    {
        0 => "Aucune session",
        1 => "1 session, 1 téléphone",
        _ => $"{SessionCount} sessions, {DeviceCount} téléphone{(DeviceCount > 1 ? "s" : string.Empty)}",
    };

    public static LaunchProfile Create(string name, IEnumerable<LaunchTarget>? targets = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Targets = targets is null ? [] : [.. targets],
    };
}
