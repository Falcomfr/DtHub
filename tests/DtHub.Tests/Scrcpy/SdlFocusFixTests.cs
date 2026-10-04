using System.Security.Cryptography;

using DtHub.Infrastructure.Scrcpy;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Scrcpy;

public sealed class SdlFocusFixTests : IDisposable
{
    private static readonly byte[] Original = "SDL d'origine"u8.ToArray();
    private static readonly byte[] Fixed = "SDL corrigé"u8.ToArray();

    private readonly string _root = Directory.CreateTempSubdirectory("dthub-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the antivirus must not make a test
            // that succeeded blush.
        }
    }

    private string Sdl => Path.Combine(_root, "SDL3.dll");

    private Task ApplyAsync(FakeProcessRunner? runner = null) =>
        new SdlFocusFix(
                Convert.ToHexStringLower(SHA256.HashData(Original)),
                Fixed,
                runner ?? new FakeProcessRunner().Respond("--version", " - SDL: 3.4.12 / 3.4.12"),
                NullLogger<SdlFocusFix>.Instance)
            .ApplyAsync(Path.Combine(_root, "scrcpy.exe"));

    [Fact]
    public async Task Remplace_le_SDL3_d_origine_par_la_version_corrigee()
    {
        File.WriteAllBytes(Sdl, Original);

        await ApplyAsync();

        Assert.Equal(Fixed, File.ReadAllBytes(Sdl));
    }

    [Fact]
    public async Task Laisse_intact_un_SDL3_d_une_autre_version_de_scrcpy()
    {
        File.WriteAllBytes(Sdl, "SDL d'une autre version"u8.ToArray());

        await ApplyAsync();

        Assert.Equal("SDL d'une autre version"u8.ToArray(), File.ReadAllBytes(Sdl));
    }

    [Fact]
    public async Task Ne_plante_pas_quand_scrcpy_tient_encore_le_fichier()
    {
        File.WriteAllBytes(Sdl, Original);

        using (new FileStream(Sdl, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await ApplyAsync();
        }

        Assert.Equal(Original, File.ReadAllBytes(Sdl));
    }

    [Fact]
    public async Task Remet_l_original_quand_scrcpy_ne_demarre_plus_avec_la_version_corrigee()
    {
        File.WriteAllBytes(Sdl, Original);

        // STATUS_DLL_NOT_FOUND, what Windows returns when a DLL cannot be loaded.
        await ApplyAsync(new FakeProcessRunner().Respond("--version", exitCode: unchecked((int)0xC0000135)));

        Assert.Equal(Original, File.ReadAllBytes(Sdl));
    }

    [Fact]
    public async Task Remet_l_original_quand_scrcpy_ne_se_lance_pas_du_tout()
    {
        File.WriteAllBytes(Sdl, Original);

        await ApplyAsync(new FakeProcessRunner().FailToLaunch());

        Assert.Equal(Original, File.ReadAllBytes(Sdl));
    }

    [Fact]
    public void Embarque_un_SDL3_corrige_qui_n_est_pas_l_original()
    {
        var embedded = SdlFocusFix.ReadFixedDll();

        Assert.Equal("MZ"u8.ToArray(), embedded[..2]);
        Assert.NotEqual(SdlFocusFix.OriginalSha256, Convert.ToHexStringLower(SHA256.HashData(embedded)));
    }
}
