using DtHub.Core.Updates;

namespace DtHub.Tests.Updates;

public sealed class UpdatePathsTests
{
    [Fact]
    public void Refuse_de_remplacer_un_executable_sorti_d_un_arbre_de_sources()
    {
        // Le lanceur de developpement republie a chaque demarrage.
        static bool Exists(string path) =>
            path.EndsWith(Path.Combine("DTHub", "DtHub.slnx"), StringComparison.Ordinal);

        Assert.False(UpdatePaths.CanReplace(
            Path.Combine("C:", "Dev", "DTHub", "build", "publish", "DtHub.exe"), Exists));
    }

    [Fact]
    public void Accepte_de_remplacer_un_executable_installe() =>
        Assert.True(UpdatePaths.CanReplace(
            Path.Combine("C:", "Programmes", "DtHub", "DtHub.exe"), _ => false));

    [Fact]
    public void Refuse_un_chemin_absent() =>
        Assert.False(UpdatePaths.CanReplace(null, _ => false));

    [Fact]
    public void Nomme_le_fichier_en_attente_par_sa_version() =>
        Assert.Equal(
            Path.Combine("dossier", "DtHub-0.2.0.exe"),
            UpdatePaths.Staged("dossier", new Version(0, 2, 0, 0)));

    [Fact]
    public void Nomme_la_note_par_sa_version() =>
        Assert.Equal(
            Path.Combine("dossier", "notes-0.2.0.txt"),
            UpdatePaths.Notes("dossier", new Version(0, 2, 0)));
}
