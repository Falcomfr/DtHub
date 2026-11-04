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
    public void Les_versions_macos_sont_celles_du_pc_et_des_archives_officielles()
    {
        var adb = DependencyManifest.Get(DependencyManifest.PlatformToolsMacKey);
        var scrcpy = DependencyManifest.Get(DependencyManifest.ScrcpyMacKey);

        // The Mac pins the same releases as the PC: adb's output is
        // parsed, and scrcpy's arguments are built for one version.
        Assert.Equal(DependencyManifest.Get(DependencyManifest.PlatformToolsKey).Version, adb.Version);
        Assert.Equal(DependencyManifest.Get(DependencyManifest.ScrcpyKey).Version, scrcpy.Version);

        Assert.Equal("dl.google.com", adb.Url.Host);
        Assert.EndsWith("-darwin.zip", adb.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("adb", adb.Executable);
        Assert.Equal("platform-tools", adb.ArchiveRootDirectory);

        Assert.Equal("github.com", scrcpy.Url.Host);
        Assert.EndsWith(".tar.gz", scrcpy.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("scrcpy", scrcpy.Executable);
        Assert.Equal($"scrcpy-macos-aarch64-v{scrcpy.Version}", scrcpy.ArchiveRootDirectory);
    }

    [Fact]
    public void Le_sdk_android_pour_macos_n_est_pas_redistribuable_non_plus()
    {
        var dependency = DependencyManifest.Get(DependencyManifest.PlatformToolsMacKey);

        Assert.False(dependency.Redistributable);
        Assert.Equal(DependencyManifest.Get(DependencyManifest.PlatformToolsKey).License, dependency.License);
    }

    [Fact]
    public void Chaque_systeme_a_son_propre_dossier_d_installation()
    {
        // Same versions, different keys: a Windows copy and a macOS
        // copy never share a folder.
        var folders = DependencyManifest.All.Values.Select(d => d.InstallDirectoryName).ToList();

        Assert.Equal(folders.Count, folders.Distinct(StringComparer.Ordinal).Count());
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
