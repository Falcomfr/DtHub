namespace DtHub.Core.Profiles;

/// <summary>Forme persistée de <c>profiles.json</c>.</summary>
public sealed class ProfileDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<StoredProfile> Profiles { get; set; } = [];

    /// <summary>Profil proposé au démarrage.</summary>
    public string? DefaultProfileId { get; set; }
}

public sealed class StoredProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset? LastUsedUtc { get; set; }
    public List<StoredTarget> Targets { get; set; } = [];

    public static StoredProfile From(LaunchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new StoredProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            LastUsedUtc = profile.LastUsedUtc,
            Targets = [.. profile.Targets.Select(StoredTarget.From)],
        };
    }

    public LaunchProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name,
        LastUsedUtc = LastUsedUtc,
        Targets = [.. Targets.Select(t => t.ToTarget())],
    };
}

public sealed class StoredTarget
{
    public string DeviceId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string? LaunchComponent { get; set; }
    public string? AppLabel { get; set; }
    public string? DeviceLabel { get; set; }
    public string? UserLabel { get; set; }

    public static StoredTarget From(LaunchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return new StoredTarget
        {
            DeviceId = target.DeviceId,
            UserId = target.UserId,
            PackageName = target.PackageName,
            LaunchComponent = target.LaunchComponent,
            AppLabel = target.AppLabel,
            DeviceLabel = target.DeviceLabel,
            UserLabel = target.UserLabel,
        };
    }

    public LaunchTarget ToTarget() => new()
    {
        DeviceId = DeviceId,
        UserId = UserId,
        PackageName = PackageName,
        LaunchComponent = LaunchComponent,
        AppLabel = AppLabel,
        DeviceLabel = DeviceLabel,
        UserLabel = UserLabel,
    };
}
