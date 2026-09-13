using DtHub.Infrastructure.Dependencies;

namespace DtHub.Tests.Dependencies;

public class DependencyManifestTests
{
    [Fact]
    public void Le_manifeste_embarque_est_lisible_et_non_vide()
    {
        Assert.NotEmpty(DependencyManifest.All);
    }

    [Fact]
    public void La_dependance_fournissant_adb_est_declaree_completement()
    {
        var dependency = DependencyManifest.Get(DependencyManifest.PlatformToolsKey);

        Assert.Equal("platform-tools", dependency.Key);
        Assert.Equal("adb.exe", dependency.Executable);
        Assert.Equal("platform-tools", dependency.ArchiveRootDirectory);
        Assert.Matches(@"^\d+\.\d+\.\d+$", dependency.Version);
        Assert.True(dependency.SizeBytes > 0);
        Assert.Matches("^[0-9a-f]{64}$", dependency.Sha256);
    }

    [Fact]
    public void La_licence_du_sdk_android_interdit_la_redistribution()
    {
        // Safeguard: if someone flips this flag, the component would
        // end up bundled inside the installer, which the license does
        // not allow.
        var dependency = DependencyManifest.Get(DependencyManifest.PlatformToolsKey);

        Assert.False(dependency.Redistributable);
        Assert.False(string.IsNullOrWhiteSpace(dependency.License));
    }

    [Fact]
    public void Toute_dependance_declaree_utilise_https_et_une_url_versionnee()
    {
        foreach (var dependency in DependencyManifest.All.Values)
        {
            Assert.Equal(Uri.UriSchemeHttps, dependency.Url.Scheme);

            // A "latest" URL would change content and would invalidate
            // the pinned fingerprint on the next upstream release.
            Assert.DoesNotContain("latest", dependency.Url.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Toute_dependance_declaree_porte_une_empreinte_et_une_licence()
    {
        foreach (var dependency in DependencyManifest.All.Values)
        {
            Assert.Matches("^[0-9a-f]{64}$", dependency.Sha256);
            Assert.False(string.IsNullOrWhiteSpace(dependency.License));
            Assert.False(string.IsNullOrWhiteSpace(dependency.Executable));
        }
    }

    [Fact]
    public void Une_cle_inconnue_echoue_explicitement()
    {
        Assert.Throws<InvalidOperationException>(() => DependencyManifest.Get("composant-inexistant"));
    }

    [Fact]
    public void Le_dossier_d_installation_est_versionne()
    {
        var dependency = DependencyManifest.Get(DependencyManifest.PlatformToolsKey);

        Assert.Equal($"platform-tools-{dependency.Version}", dependency.InstallDirectoryName);
    }
}
