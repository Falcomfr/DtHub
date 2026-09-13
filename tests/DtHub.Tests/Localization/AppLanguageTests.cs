using DtHub.Core.Localization;

namespace DtHub.Tests.Localization;

/// <summary>
/// La règle de choix de la langue : Windows par défaut, l'anglais quand elle
/// n'est pas traduite, et un réglage qui l'emporte sur les deux.
/// </summary>
public sealed class AppLanguageTests
{
    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("fr", "fr")]
    [InlineData("fr-BE", "fr")]
    [InlineData("es-ES", "es")]
    [InlineData("es-419", "es")]
    [InlineData("en-US", "en")]
    public void Windows_traduit_donne_sa_langue(string windows, string attendu)
        => Assert.Equal(attendu, AppLanguage.Choose(preferred: null, windows));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("pt-BR")]
    [InlineData("zh-Hans-CN")]
    [InlineData("")]
    [InlineData(null)]
    public void Windows_non_traduit_donne_l_anglais(string? windows)
        => Assert.Equal("en", AppLanguage.Choose(preferred: null, windows));

    [Fact]
    public void Le_reglage_l_emporte_sur_Windows()
        => Assert.Equal("es", AppLanguage.Choose(preferred: "es", windows: "fr-FR"));

    [Fact]
    public void Un_reglage_vide_rend_la_main_a_Windows()
        => Assert.Equal("fr", AppLanguage.Choose(preferred: "  ", windows: "fr-FR"));

    /// <summary>
    /// Un fichier de réglages abîmé ne doit pas coûter la langue de Windows :
    /// une valeur qu'on ne sert pas se traite comme une absence de valeur.
    /// </summary>
    [Fact]
    public void Un_reglage_inconnu_rend_la_main_a_Windows()
        => Assert.Equal("fr", AppLanguage.Choose(preferred: "de", windows: "fr-FR"));

    [Fact]
    public void Rien_du_tout_donne_l_anglais()
        => Assert.Equal("en", AppLanguage.Choose(preferred: null, windows: null));

    [Theory]
    [InlineData("fr", true)]
    [InlineData("es-MX", true)]
    [InlineData("de", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Serves_dit_si_la_langue_est_traduite(string? culture, bool attendu)
        => Assert.Equal(attendu, AppLanguage.Serves(culture));

    /// <summary>La langue neutre doit être servie, sans quoi le repli est vide.</summary>
    [Fact]
    public void La_langue_neutre_fait_partie_des_langues_servies()
        => Assert.Contains(AppLanguage.Neutral, AppLanguage.Supported);
    [Fact]
    public void Revenir_a_la_langue_affichee_ne_demande_plus_de_relancer()
    {
        // Le défaut d'origine : le message se levait à tout changement et ne
        // redescendait jamais. Qui changeait de langue puis se ravisait gardait
        // l'invitation à relancer, pour une application qui n'avait plus rien
        // à changer.
        Assert.True(AppLanguage.NeedsRestart("es", "fr-FR", inForce: "fr"));
        Assert.False(AppLanguage.NeedsRestart("fr", "fr-FR", inForce: "fr"));
    }

    [Fact]
    public void Suivre_windows_ne_demande_rien_quand_windows_parle_deja_cette_langue()
    {
        Assert.False(AppLanguage.NeedsRestart(string.Empty, "fr-FR", inForce: "fr"));
        Assert.True(AppLanguage.NeedsRestart(string.Empty, "es-ES", inForce: "fr"));
    }

    [Fact]
    public void Une_langue_non_traduite_retombe_sur_la_neutre_des_deux_cotes()
    {
        // Choisir le japonais alors que l'application est en anglais ne change
        // rien : les deux se résolvent en anglais.
        Assert.False(AppLanguage.NeedsRestart("ja", "ja-JP", inForce: "en"));
    }

    [Fact]
    public void Une_variante_regionale_vaut_sa_langue()
    {
        Assert.False(AppLanguage.NeedsRestart("fr-BE", "en-US", inForce: "fr"));
    }

}
