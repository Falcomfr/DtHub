using DtHub.Core.Dofus;
using DtHub.Core.IPhone;
using DtHub.Core.Settings;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Settings;

public sealed class IosAccountTests : IDisposable
{
    private const string Udid = "00008132-001031940A05801C";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-ios-" + Guid.NewGuid().ToString("N"));

    private readonly SettingsService _service;

    public IosAccountTests() =>
        _service = new SettingsService(new JsonDocumentStore<AppSettingsDocument>(
            Path.Combine(_directory, "settings.json"), NullLogger.Instance));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static IPhoneDevice Phone(string name = "iPhone de Test") => new(Udid, name, "27.0", "USB", "iPhone");

    [Fact]
    public async Task Un_iPhone_devient_un_compte_a_sa_premiere_vue()
    {
        await _service.UpdateAsync(s => s.Instances.Add(
            new StoredInstance { DeviceId = "phone", PackageName = "p", UserName = "u" }));

        var compte = await _service.EnsureIosAccountAsync(Phone());

        Assert.Equal($"{Udid}|0|ios", compte.Key);
        Assert.Equal(DevicePlatform.Ios, compte.Platform);
        Assert.True(compte.IsIos);

        var stored = (await _service.GetAsync()).Instances.OrderBy(i => i.Order).Last();
        Assert.Equal(compte.Key, stored.Key);
    }

    [Fact]
    public async Task Un_iPhone_deja_connu_garde_son_compte_et_son_nom_choisi()
    {
        var premier = await _service.EnsureIosAccountAsync(Phone());
        await _service.RenameInstanceAsync(premier.Key, "Mon iPhone");

        var second = await _service.EnsureIosAccountAsync(Phone("Autre nom"));

        var instances = (await _service.GetAsync()).Instances;
        var stored = Assert.Single(instances);
        Assert.Equal("Mon iPhone", stored.CustomName);
        Assert.Equal("Autre nom", stored.DeviceName);
        Assert.Equal("Mon iPhone", second.CustomName);
    }

    [Fact]
    public async Task Le_balayage_des_profils_Android_n_oublie_jamais_un_compte_iOS()
    {
        _ = await _service.EnsureIosAccountAsync(Phone());

        var oublies = await _service.ForgetMissingProfilesAsync(
            new Dictionary<string, IReadOnlyList<int>> { [Udid] = [1], ["phone"] = [0] },
            new Dictionary<string, IReadOnlyList<int>> { [Udid] = [0] });

        Assert.Equal(0, oublies);
        Assert.Single((await _service.GetAsync()).Instances);
    }

    [Fact]
    public async Task La_plateforme_et_l_identifiant_du_jeu_survivent_a_la_relecture()
    {
        var compte = await _service.EnsureIosAccountAsync(Phone());
        await _service.SetIosBundleIdAsync(compte.Key, "com.ankama.dofustouch");

        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonDocumentStore<AppSettingsDocument>(path, NullLogger.Instance);
        var relu = new SettingsService(store);
        var stored = Assert.Single((await relu.GetAsync()).Instances);

        Assert.Equal(DevicePlatform.Ios, stored.Platform);
        Assert.Equal("com.ankama.dofustouch", stored.IosBundleId);

        var ancien = store.Deserialize(
            """{"instances":[{"deviceId":"d","userId":0,"packageName":"p"}]}""");
        Assert.Equal(DevicePlatform.Android, ancien!.Instances[0].Platform);
    }
}
