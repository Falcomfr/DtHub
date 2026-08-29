using DtHub.Core.Profiles;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Profiles;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-profiles-" + Guid.NewGuid().ToString("N"));

    private readonly JsonDocumentStore<ProfileDocument> _store;
    private readonly ProfileService _service;

    public ProfileServiceTests()
    {
        _store = new JsonDocumentStore<ProfileDocument>(
            Path.Combine(_directory, "profiles.json"), NullLogger.Instance);

        _service = new ProfileService(_store);
    }

    public void Dispose()
    {
        _service.Dispose();
        _store.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static LaunchTarget Target(
        string device = "MATERIEL123",
        int user = 0,
        string package = "com.ankama.dofustouch") => new()
        {
            DeviceId = device,
            UserId = user,
            PackageName = package,
            LaunchComponent = $"{package}/.MainActivity",
            AppLabel = "DOFUS Touch",
            DeviceLabel = "Xiaomi 13T Pro",
            UserLabel = user == 0 ? "Principal" : "Clone",
        };

    [Fact]
    public async Task Un_depot_neuf_ne_contient_aucun_profil()
    {
        Assert.Empty(await _service.GetAllAsync(CancellationToken.None));
        Assert.Null(await _service.GetDefaultAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Un_profil_cree_est_relu_avec_ses_sessions()
    {
        var created = await _service.CreateAsync("Jeux", [Target(), Target(user: 999)], CancellationToken.None);

        var read = Assert.Single(await _service.GetAllAsync(CancellationToken.None));

        Assert.Equal(created.Id, read.Id);
        Assert.Equal("Jeux", read.Name);
        Assert.Equal(2, read.SessionCount);
        Assert.Equal(1, read.DeviceCount);
    }

    [Fact]
    public async Task Deux_profils_ne_peuvent_pas_porter_le_meme_nom()
    {
        await _service.CreateAsync("Jeux", null, CancellationToken.None);
        await _service.CreateAsync("Jeux", null, CancellationToken.None);
        await _service.CreateAsync("jeux", null, CancellationToken.None);

        var names = (await _service.GetAllAsync(CancellationToken.None)).Select(p => p.Name).ToList();

        Assert.Equal(["Jeux", "Jeux 2", "jeux 3"], names);
    }

    [Fact]
    public async Task Une_session_en_double_n_est_pas_ajoutee_deux_fois()
    {
        var profile = await _service.CreateAsync("Jeux", [Target(), Target()], CancellationToken.None);

        Assert.Equal(1, profile.SessionCount);

        await _service.AddTargetAsync(profile.Id, Target(), CancellationToken.None);

        Assert.Equal(1, (await _service.FindAsync(profile.Id, CancellationToken.None))!.SessionCount);
    }

    [Fact]
    public async Task Le_meme_paquet_sur_deux_profils_android_compte_pour_deux_sessions()
    {
        var profile = await _service.CreateAsync(
            "Jeux", [Target(user: 0), Target(user: 999)], CancellationToken.None);

        Assert.Equal(2, profile.SessionCount);
        Assert.Equal(2, profile.Targets.Select(t => t.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task L_ordre_des_sessions_peut_etre_modifie()
    {
        var a = Target(package: "com.a");
        var b = Target(package: "com.b");
        var c = Target(package: "com.c");

        var profile = await _service.CreateAsync("Jeux", [a, b, c], CancellationToken.None);

        await _service.MoveTargetAsync(profile.Id, c.Key, 0, CancellationToken.None);

        var reordered = await _service.FindAsync(profile.Id, CancellationToken.None);

        Assert.Equal(["com.c", "com.a", "com.b"], reordered!.Targets.Select(t => t.PackageName));
    }

    [Fact]
    public async Task Un_deplacement_hors_limites_place_la_session_a_l_extremite()
    {
        var a = Target(package: "com.a");
        var b = Target(package: "com.b");

        var profile = await _service.CreateAsync("Jeux", [a, b], CancellationToken.None);

        await _service.MoveTargetAsync(profile.Id, a.Key, 99, CancellationToken.None);

        var reordered = await _service.FindAsync(profile.Id, CancellationToken.None);

        Assert.Equal(["com.b", "com.a"], reordered!.Targets.Select(t => t.PackageName));
    }

    [Fact]
    public async Task Une_session_peut_etre_retiree()
    {
        var a = Target(package: "com.a");
        var b = Target(package: "com.b");
        var profile = await _service.CreateAsync("Jeux", [a, b], CancellationToken.None);

        await _service.RemoveTargetAsync(profile.Id, a.Key, CancellationToken.None);

        var updated = await _service.FindAsync(profile.Id, CancellationToken.None);
        Assert.Equal("com.b", Assert.Single(updated!.Targets).PackageName);
    }

    [Fact]
    public async Task Un_profil_duplique_a_les_memes_sessions_et_une_identite_propre()
    {
        var profile = await _service.CreateAsync("Jeux", [Target(), Target(user: 999)], CancellationToken.None);

        var copy = await _service.DuplicateAsync(profile.Id, CancellationToken.None);

        Assert.NotNull(copy);
        Assert.NotEqual(profile.Id, copy.Id);
        Assert.Equal("Jeux (copie)", copy.Name);
        Assert.Equal(
            profile.Targets.Select(t => t.Key),
            copy.Targets.Select(t => t.Key));
    }

    [Fact]
    public async Task Dupliquer_un_profil_inexistant_ne_cree_rien()
    {
        Assert.Null(await _service.DuplicateAsync("inexistant", CancellationToken.None));
        Assert.Empty(await _service.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Un_profil_renomme_garde_son_identite_et_ses_sessions()
    {
        var profile = await _service.CreateAsync("Jeux", [Target()], CancellationToken.None);

        await _service.RenameAsync(profile.Id, "Soirée", CancellationToken.None);

        var renamed = await _service.FindAsync(profile.Id, CancellationToken.None);

        Assert.Equal("Soirée", renamed!.Name);
        Assert.Equal(1, renamed.SessionCount);
    }

    [Fact]
    public async Task Un_profil_supprime_disparait_et_perd_son_statut_par_defaut()
    {
        var profile = await _service.CreateAsync("Jeux", [Target()], CancellationToken.None);
        await _service.SetDefaultAsync(profile.Id, CancellationToken.None);

        await _service.DeleteAsync(profile.Id, CancellationToken.None);

        Assert.Empty(await _service.GetAllAsync(CancellationToken.None));
        Assert.Null(await _service.GetDefaultAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Le_profil_par_defaut_explicite_prime_sur_le_dernier_utilise()
    {
        var jeux = await _service.CreateAsync("Jeux", null, CancellationToken.None);
        var travail = await _service.CreateAsync("Travail", null, CancellationToken.None);

        await _service.TouchAsync(travail.Id, CancellationToken.None);
        await _service.SetDefaultAsync(jeux.Id, CancellationToken.None);

        Assert.Equal(jeux.Id, (await _service.GetDefaultAsync(CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task Sans_profil_par_defaut_le_dernier_utilise_est_propose()
    {
        await _service.CreateAsync("Jeux", null, CancellationToken.None);
        var travail = await _service.CreateAsync("Travail", null, CancellationToken.None);

        await _service.TouchAsync(travail.Id, CancellationToken.None);

        Assert.Equal(travail.Id, (await _service.GetDefaultAsync(CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task Oublier_un_appareil_retire_ses_sessions_de_tous_les_profils()
    {
        var jeux = await _service.CreateAsync(
            "Jeux",
            [Target("MATERIEL123"), Target("MATERIEL456", package: "com.b")],
            CancellationToken.None);

        var travail = await _service.CreateAsync(
            "Travail", [Target("MATERIEL123", package: "com.c")], CancellationToken.None);

        await _service.RemoveDeviceEverywhereAsync("MATERIEL123", CancellationToken.None);

        Assert.Equal(
            "MATERIEL456",
            Assert.Single((await _service.FindAsync(jeux.Id, CancellationToken.None))!.Targets).DeviceId);

        Assert.Empty((await _service.FindAsync(travail.Id, CancellationToken.None))!.Targets);
    }

    [Fact]
    public async Task Le_resume_du_profil_est_lisible()
    {
        var vide = await _service.CreateAsync("Vide", null, CancellationToken.None);
        var une = await _service.CreateAsync("Une", [Target()], CancellationToken.None);
        var trois = await _service.CreateAsync(
            "Trois",
            [Target(), Target(user: 999), Target("MATERIEL456", package: "com.b")],
            CancellationToken.None);

        Assert.Equal("Aucune session", vide.Summary);
        Assert.Equal("1 session, 1 téléphone", une.Summary);
        Assert.Equal("3 sessions, 2 téléphones", trois.Summary);
    }

    [Fact]
    public async Task Le_libelle_d_une_session_reprend_appareil_application_et_profil()
    {
        var profile = await _service.CreateAsync("Jeux", [Target(user: 999)], CancellationToken.None);

        Assert.Equal(
            "Xiaomi 13T Pro - DOFUS Touch - Clone",
            Assert.Single(profile.Targets).DisplayName);
    }

    [Fact]
    public async Task Un_fichier_de_profils_corrompu_ne_fait_pas_perdre_l_application()
    {
        await _service.CreateAsync("Jeux", [Target()], CancellationToken.None);

        await File.WriteAllTextAsync(_store.FilePath, "{ cassé", CancellationToken.None);

        Assert.Empty(await _service.GetAllAsync(CancellationToken.None));

        // On peut repartir immédiatement d'un profil neuf.
        await _service.CreateAsync("Jeux", [Target()], CancellationToken.None);
        Assert.Single(await _service.GetAllAsync(CancellationToken.None));
    }
}
