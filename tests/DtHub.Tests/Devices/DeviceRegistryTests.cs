using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Infrastructure.Devices;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Devices;

public sealed class DeviceRegistryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-registry-" + Guid.NewGuid().ToString("N"));

    private readonly JsonDocumentStore<DeviceRegistryDocument> _store;
    private readonly DeviceRegistry _registry;

    public DeviceRegistryTests()
    {
        _store = new JsonDocumentStore<DeviceRegistryDocument>(
            Path.Combine(_directory, "devices.json"), NullLogger.Instance);

        _registry = new DeviceRegistry(_store);
    }

    public void Dispose()
    {
        _registry.Dispose();
        _store.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static AndroidDevice Device(string id = "MATERIEL123", string serial = "USB0001") => new()
    {
        Id = id,
        Serial = serial,
        State = AdbDeviceState.Device,
        ConnectionKind = AdbConnectionKind.Usb,
        Manufacturer = "Xiaomi",
        Model = "23078RKD5G",
        MarketName = "13T Pro",
        AndroidVersion = "14",
        SdkVersion = 34,
    };

    [Fact]
    public async Task Un_registre_neuf_est_vide()
    {
        Assert.Empty(await _registry.GetKnownAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Un_appareil_ajoute_est_relu_avec_ses_caracteristiques()
    {
        await _registry.UpsertAsync(Device(), CancellationToken.None);

        var known = Assert.Single(await _registry.GetKnownAsync(CancellationToken.None));

        Assert.Equal("MATERIEL123", known.Id);
        Assert.Equal("13T Pro", known.MarketName);
        Assert.Equal(34, known.SdkVersion);
    }

    [Fact]
    public async Task Un_appareil_relu_est_hors_ligne_tant_qu_il_n_a_pas_ete_revu()
    {
        await _registry.UpsertAsync(Device(), CancellationToken.None);

        var known = Assert.Single(await _registry.GetKnownAsync(CancellationToken.None));

        // L'état ne se persiste pas : le croire connecté au démarrage
        // conduirait à afficher des appareils fantômes.
        Assert.False(known.IsConnected);
        Assert.Equal(AdbDeviceState.Offline, known.State);
    }

    [Fact]
    public async Task Un_meme_appareil_n_est_pas_dupplique()
    {
        await _registry.UpsertAsync(Device(), CancellationToken.None);
        await _registry.UpsertAsync(Device() with { Serial = "192.168.1.25:37845" }, CancellationToken.None);

        var known = Assert.Single(await _registry.GetKnownAsync(CancellationToken.None));
        Assert.Equal("192.168.1.25:37845", known.Serial);
    }

    [Fact]
    public async Task Le_nom_personnalise_survit_a_une_redecouverte()
    {
        await _registry.UpsertAsync(Device(), CancellationToken.None);
        await _registry.RenameAsync("MATERIEL123", "Téléphone du salon", CancellationToken.None);

        await _registry.UpsertAsync(Device(), CancellationToken.None);

        var known = Assert.Single(await _registry.GetKnownAsync(CancellationToken.None));
        Assert.Equal("Téléphone du salon", known.CustomName);
    }

    [Fact]
    public async Task Un_nom_vide_retablit_le_nom_detecte()
    {
        await _registry.UpsertAsync(Device(), CancellationToken.None);
        await _registry.RenameAsync("MATERIEL123", "Salon", CancellationToken.None);
        await _registry.RenameAsync("MATERIEL123", "   ", CancellationToken.None);

        var known = Assert.Single(await _registry.GetKnownAsync(CancellationToken.None));

        Assert.Null(known.CustomName);
        Assert.Equal("13T Pro", known.DisplayName);
    }

    [Fact]
    public async Task Un_seul_appareil_peut_etre_principal()
    {
        await _registry.UpsertRangeAsync([Device(), Device("MATERIEL456", "USB0002")], CancellationToken.None);

        await _registry.SetPrimaryAsync("MATERIEL123", CancellationToken.None);
        await _registry.SetPrimaryAsync("MATERIEL456", CancellationToken.None);

        var known = await _registry.GetKnownAsync(CancellationToken.None);

        Assert.Single(known, d => d.IsPrimary);
        Assert.True(known.Single(d => d.Id == "MATERIEL456").IsPrimary);
    }

    [Fact]
    public async Task Oublier_un_appareil_le_retire_definitivement()
    {
        await _registry.UpsertRangeAsync([Device(), Device("MATERIEL456", "USB0002")], CancellationToken.None);

        await _registry.ForgetAsync("MATERIEL123", CancellationToken.None);

        var known = await _registry.GetKnownAsync(CancellationToken.None);
        Assert.Equal("MATERIEL456", Assert.Single(known).Id);
    }

    [Fact]
    public async Task L_adresse_de_reconnexion_n_est_pas_perdue_par_une_mise_a_jour_usb()
    {
        await _registry.UpsertAsync(
            Device() with { LastKnownAddress = "192.168.1.25", LastKnownPort = 37845 },
            CancellationToken.None);

        await _registry.UpsertAsync(Device(), CancellationToken.None);

        var known = Assert.Single(await _registry.GetKnownAsync(CancellationToken.None));
        Assert.Equal("192.168.1.25:37845", known.ReconnectAddress);
    }

    [Fact]
    public async Task Le_fichier_ecrit_porte_une_version_de_schema()
    {
        await _registry.UpsertAsync(Device(), CancellationToken.None);

        var json = await File.ReadAllTextAsync(_store.FilePath, CancellationToken.None);

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
    }
}
