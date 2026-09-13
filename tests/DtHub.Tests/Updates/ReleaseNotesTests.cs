using DtHub.Core.Updates;

namespace DtHub.Tests.Updates;

public sealed class ReleaseNotesTests
{
    [Fact]
    public void Enleve_les_dieses_des_titres() =>
        Assert.Equal("Ajoute", ReleaseNotes.Readable("### Ajoute"));

    [Theory]
    [InlineData("- Une chose.")]
    [InlineData("* Une chose.")]
    [InlineData("  + Une chose.")]
    public void Rend_une_puce_lisible(string line) =>
        Assert.Equal("•  Une chose.", ReleaseNotes.Readable(line));

    [Fact]
    public void Enleve_le_gras_et_le_code() =>
        Assert.Equal(
            "Le fichier settings.json est important.",
            ReleaseNotes.Readable("Le fichier `settings.json` est **important**."));

    [Fact]
    public void Garde_le_texte_d_un_lien_et_jette_son_adresse() =>
        Assert.Equal(
            "Voir la documentation ici.",
            ReleaseNotes.Readable("Voir la [documentation](https://exemple/doc) ici."));

    [Fact]
    public void Ramene_les_blancs_multiples_a_un_seul()
    {
        // The feed puts two or three between its blocks, which would
        // punch holes in a panel of just a few lines.
        var notes = ReleaseNotes.Readable("Un.\n\n\n\nDeux.");

        Assert.Equal("Un.\n\nDeux.", notes);
    }

    [Fact]
    public void Rend_une_chaine_vide_pour_une_note_absente()
    {
        Assert.Empty(ReleaseNotes.Readable(null));
        Assert.Empty(ReleaseNotes.Readable("   \n  \n"));
    }
}
