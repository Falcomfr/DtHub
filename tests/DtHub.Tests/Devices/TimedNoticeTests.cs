using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// Un avis qui cesse d'être vrai. Sa péremption tournait depuis le
/// balayage, c'est-à-dire depuis la seule chose qui s'arrête quand on
/// cache le panneau.
/// </summary>
public sealed class TimedNoticeTests
{
    private static readonly DateTimeOffset Midi = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Vie = TimeSpan.FromSeconds(45);

    [Fact]
    public void Un_avis_sans_texte_n_existe_pas()
    {
        Assert.False(default(TimedNotice).Exists);
        Assert.False(TimedNotice.Raised(null, Midi).Exists);
        Assert.False(TimedNotice.Raised("   ", Midi).Exists);
    }

    [Fact]
    public void Un_avis_frais_est_vivant()
    {
        var avis = TimedNotice.Raised("La fenêtre revient.", Midi);

        Assert.True(avis.IsLiveAt(Midi, Vie));
        Assert.True(avis.IsLiveAt(Midi.AddSeconds(44), Vie));
    }

    [Fact]
    public void Passee_sa_vie_l_avis_se_tait()
    {
        var avis = TimedNotice.Raised("La fenêtre revient.", Midi);

        Assert.False(avis.IsLiveAt(Midi.AddSeconds(45), Vie));
        Assert.False(avis.IsLiveAt(Midi.AddHours(3), Vie));
    }

    [Fact]
    public void Le_panneau_cache_ne_prolonge_plus_rien()
    {
        // C'est le défaut : l'avis restait mot pour mot au retour,
        // parce que rien ne tournait pendant l'absence.
        var avis = TimedNotice.Raised("Le jeu est resté ouvert sur le téléphone.", Midi);

        Assert.Null(avis.LineAt(Midi.AddMinutes(90), Vie));
    }

    [Fact]
    public void Un_avis_vivant_donne_sa_ligne_de_bandeau()
    {
        var avis = TimedNotice.Raised("Le jeu est resté ouvert.", Midi);
        var ligne = avis.LineAt(Midi.AddSeconds(10), Vie);

        Assert.NotNull(ligne);
        Assert.Equal("Le jeu est resté ouvert.", ligne!.Value.Text);
        Assert.Equal(HealthSeverity.Warning, ligne.Value.Severity);
    }

    [Fact]
    public void Un_avis_vide_ne_donne_aucune_ligne()
    {
        Assert.Null(default(TimedNotice).LineAt(Midi, Vie));
    }

    [Fact]
    public void Lire_l_avis_ne_le_prolonge_pas()
    {
        // Il porte son instant, pas un compte à rebours : deux
        // lectures rendent la même réponse.
        var avis = TimedNotice.Raised("Un mot.", Midi);

        Assert.True(avis.IsLiveAt(Midi.AddSeconds(30), Vie));
        Assert.True(avis.IsLiveAt(Midi.AddSeconds(30), Vie));
        Assert.False(avis.IsLiveAt(Midi.AddSeconds(60), Vie));
    }

    [Fact]
    public void Une_gravite_choisie_est_gardee()
    {
        var avis = new TimedNotice("Batterie à plat.", Midi, HealthSeverity.Serious);

        Assert.Equal(HealthSeverity.Serious, avis.LineAt(Midi, Vie)!.Value.Severity);
    }
}
