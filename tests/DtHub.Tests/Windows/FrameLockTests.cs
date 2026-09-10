using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

/// <summary>
/// Ce que le cadenas veut dire pour un compte logé dans le cadre à onglets.
///
/// Les deux bascules étant indépendantes, un compte pouvait être verrouillé et
/// logé, et le cadre le redimensionnait quand même : le verrou n'y protégeait
/// de rien alors qu'il promet le contraire.
/// </summary>
public class FrameLockTests
{
    [Fact]
    public void Un_compte_loge_et_verrouille_fige_le_cadre()
    {
        Assert.True(FrameLock.Freezes(["principal"], ["principal", "xspace"]));
    }

    [Fact]
    public void Un_seul_cadenas_suffit_meme_si_les_autres_sont_libres()
    {
        // C'est le prix assumé de la règle : un verrou est une protection, et
        // un voisin ne lève pas la protection d'un autre.
        Assert.True(FrameLock.Freezes(["xspace"], ["principal", "xspace", "second"]));
    }

    [Fact]
    public void Un_compte_verrouille_reste_libre_hors_du_cadre()
    {
        Assert.False(FrameLock.Freezes(["principal"], ["xspace"]));
    }

    [Fact]
    public void Sans_cadenas_le_cadre_suit_les_placements()
    {
        Assert.False(FrameLock.Freezes([], ["principal", "xspace"]));
    }

    [Fact]
    public void Un_cadre_vide_n_est_jamais_fige()
    {
        Assert.False(FrameLock.Freezes(["principal"], []));
    }

    [Fact]
    public void La_comparaison_des_clefs_est_exacte()
    {
        // Les clefs viennent des réglages et distinguent la casse : les
        // rapprocher sans y prendre garde figerait un cadre au hasard.
        Assert.False(FrameLock.Freezes(["Principal"], ["principal"]));
    }
}
