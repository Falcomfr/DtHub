using DtHub.Core.Storage;

namespace DtHub.Core.Profiles;

/// <summary>
/// Gestion des profils de lancement : création, renommage, duplication,
/// suppression, et modification de la liste des sessions. Chaque opération
/// écrit immédiatement, il n'y a rien à enregistrer explicitement.
/// </summary>
public sealed class ProfileService : IDisposable
{
    private readonly IDocumentStore<ProfileDocument> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProfileService(IDocumentStore<ProfileDocument> store) => _store = store;

    public async Task<IReadOnlyList<LaunchProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        return [.. document.Profiles
            .Where(p => !string.IsNullOrEmpty(p.Id))
            .Select(p => p.ToProfile())];
    }

    public async Task<LaunchProfile?> FindAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        return document.Profiles
            .Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal))
            ?.ToProfile();
    }

    /// <summary>
    /// Profil proposé au démarrage : celui marqué par défaut, sinon le dernier
    /// utilisé, sinon le premier de la liste.
    /// </summary>
    public async Task<LaunchProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        var explicitDefault = document.Profiles
            .Find(p => string.Equals(p.Id, document.DefaultProfileId, StringComparison.Ordinal));

        if (explicitDefault is not null)
        {
            return explicitDefault.ToProfile();
        }

        return document.Profiles
            .OrderByDescending(p => p.LastUsedUtc ?? DateTimeOffset.MinValue)
            .FirstOrDefault()
            ?.ToProfile();
    }

    public Task SetDefaultAsync(string? profileId, CancellationToken cancellationToken = default) =>
        MutateAsync(document => document.DefaultProfileId = profileId, cancellationToken);

    /// <summary>
    /// Crée un profil. Le nom est rendu unique par ajout d'un numéro, plutôt
    /// que d'être refusé : l'utilisateur n'a pas à deviner ce qui existe déjà.
    /// </summary>
    public async Task<LaunchProfile> CreateAsync(
        string name,
        IEnumerable<LaunchTarget>? targets = null,
        CancellationToken cancellationToken = default)
    {
        LaunchProfile created = null!;

        await MutateAsync(document =>
        {
            var profile = LaunchProfile.Create(
                MakeUniqueName(document, name, excludedId: null),
                Deduplicate(targets));

            document.Profiles.Add(StoredProfile.From(profile));
            created = profile;
        }, cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<LaunchProfile?> DuplicateAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        LaunchProfile? copy = null;

        await MutateAsync(document =>
        {
            var source = document.Profiles.Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
            if (source is null)
            {
                return;
            }

            var profile = LaunchProfile.Create(
                MakeUniqueName(document, $"{source.Name} (copie)", excludedId: null),
                source.Targets.Select(t => t.ToTarget()));

            document.Profiles.Add(StoredProfile.From(profile));
            copy = profile;
        }, cancellationToken).ConfigureAwait(false);

        return copy;
    }

    public Task RenameAsync(string profileId, string name, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var profile = document.Profiles.Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
            if (profile is not null && !string.IsNullOrWhiteSpace(name))
            {
                profile.Name = MakeUniqueName(document, name, excludedId: profileId);
            }
        }, cancellationToken);

    public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            document.Profiles.RemoveAll(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));

            if (string.Equals(document.DefaultProfileId, profileId, StringComparison.Ordinal))
            {
                document.DefaultProfileId = null;
            }
        }, cancellationToken);

    /// <summary>Remplace la liste des sessions d'un profil, doublons écartés.</summary>
    public Task SetTargetsAsync(
        string profileId,
        IEnumerable<LaunchTarget> targets,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var profile = document.Profiles.Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
            if (profile is not null)
            {
                profile.Targets = [.. Deduplicate(targets).Select(StoredTarget.From)];
            }
        }, cancellationToken);

    /// <summary>Ajoute une session si elle n'est pas déjà présente.</summary>
    public Task AddTargetAsync(
        string profileId,
        LaunchTarget target,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var profile = document.Profiles.Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
            if (profile is null)
            {
                return;
            }

            var key = target.Key;
            if (!profile.Targets.Exists(t => string.Equals(t.ToTarget().Key, key, StringComparison.Ordinal)))
            {
                profile.Targets.Add(StoredTarget.From(target));
            }
        }, cancellationToken);

    public Task RemoveTargetAsync(
        string profileId,
        string targetKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var profile = document.Profiles.Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
            profile?.Targets.RemoveAll(t => string.Equals(t.ToTarget().Key, targetKey, StringComparison.Ordinal));
        }, cancellationToken);

    /// <summary>
    /// Déplace une session dans l'ordre du profil. L'ordre détermine
    /// l'empilement des fenêtres et le parcours au clavier.
    /// </summary>
    public Task MoveTargetAsync(
        string profileId,
        string targetKey,
        int newIndex,
        CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var profile = document.Profiles.Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
            if (profile is null)
            {
                return;
            }

            var index = profile.Targets.FindIndex(
                t => string.Equals(t.ToTarget().Key, targetKey, StringComparison.Ordinal));

            if (index < 0)
            {
                return;
            }

            var target = profile.Targets[index];
            profile.Targets.RemoveAt(index);
            profile.Targets.Insert(Math.Clamp(newIndex, 0, profile.Targets.Count), target);
        }, cancellationToken);

    /// <summary>Marque le profil comme utilisé maintenant.</summary>
    public Task TouchAsync(string profileId, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var profile = document.Profiles.Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
            if (profile is not null)
            {
                profile.LastUsedUtc = DateTimeOffset.UtcNow;
            }
        }, cancellationToken);

    /// <summary>
    /// Retire d'un coup toutes les sessions visant un appareil oublié, sur
    /// tous les profils. Sans cela, un profil garderait des sessions
    /// impossibles à ouvrir.
    /// </summary>
    public Task RemoveDeviceEverywhereAsync(string deviceId, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            foreach (var profile in document.Profiles)
            {
                profile.Targets.RemoveAll(t => string.Equals(t.DeviceId, deviceId, StringComparison.Ordinal));
            }
        }, cancellationToken);

    public void Dispose() => _gate.Dispose();

    private static IReadOnlyList<LaunchTarget> Deduplicate(IEnumerable<LaunchTarget>? targets) =>
        targets is null ? [] : [.. targets.DistinctBy(t => t.Key, StringComparer.Ordinal)];

    /// <summary>
    /// Rend le nom unique en lui ajoutant un numéro. Deux profils portant le
    /// même nom seraient impossibles à distinguer dans le sélecteur.
    /// </summary>
    private static string MakeUniqueName(ProfileDocument document, string name, string? excludedId)
    {
        var candidate = name.Trim();
        if (candidate.Length == 0)
        {
            candidate = "Profil";
        }

        var taken = document.Profiles
            .Where(p => !string.Equals(p.Id, excludedId, StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        if (!taken.Contains(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var numbered = $"{candidate} {suffix}";
            if (!taken.Contains(numbered))
            {
                return numbered;
            }
        }
    }

    private async Task MutateAsync(Action<ProfileDocument> mutate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            mutate(document);
            document.SchemaVersion = ProfileDocument.CurrentSchemaVersion;
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
