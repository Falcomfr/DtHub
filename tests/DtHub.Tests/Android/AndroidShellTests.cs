using DtHub.Core.Android;

namespace DtHub.Tests.Android;

public sealed class AndroidShellTests
{
    /// <summary>
    /// The case that cost us the measurement: "pm create-user
    /// Compte 3" created a profile named "Compte". ADB glues the
    /// arguments back together with spaces, and the device's shell
    /// splits them again.
    /// </summary>
    [Fact]
    public void Un_nom_a_espace_reste_entier() =>
        Assert.Equal("'Compte 3'", AndroidShell.Quote("Compte 3"));

    [Fact]
    public void Un_nom_simple_est_cite_quand_meme() =>
        Assert.Equal("'Compte'", AndroidShell.Quote("Compte"));

    /// <summary>
    /// An apostrophe would close the quoting. We close it, slip in
    /// an escaped one, and reopen: this is the only safe way in a
    /// shell.
    /// </summary>
    [Fact]
    public void Une_apostrophe_ne_casse_pas_la_citation() =>
        Assert.Equal(@"'L'\''autre'", AndroidShell.Quote("L'autre"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Un_texte_vide_reste_un_argument(string? value) =>
        Assert.Equal("''", AndroidShell.Quote(value));

    [Fact]
    public void Rien_d_autre_n_est_interprete() =>
        Assert.Equal("'a $b `c` ;d'", AndroidShell.Quote("a $b `c` ;d"));
}
