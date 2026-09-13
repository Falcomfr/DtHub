using DtHub.Core.Localization;

namespace DtHub.Tests.Localization;

/// <summary>
/// The rule for choosing the language: Windows by default, English
/// when it is not translated, and a setting that overrides both.
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
    /// A corrupted settings file must not cost the Windows language: a
    /// value we do not serve is treated as if no value were set.
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

    /// <summary>
    /// The neutral language must be served, otherwise the fallback is
    /// empty.
    /// </summary>
    [Fact]
    public void La_langue_neutre_fait_partie_des_langues_servies()
        => Assert.Contains(AppLanguage.Neutral, AppLanguage.Supported);
    [Fact]
    public void Revenir_a_la_langue_affichee_ne_demande_plus_de_relancer()
    {
        // The original flaw: the message used to pop up on every change
        // and never come back down. Anyone who switched language and
        // then changed their mind kept the restart prompt, for an app
        // that had nothing left to change.
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
        // Choosing Japanese while the application is in English changes
        // nothing: both resolve to English.
        Assert.False(AppLanguage.NeedsRestart("ja", "ja-JP", inForce: "en"));
    }

    [Fact]
    public void Une_variante_regionale_vaut_sa_langue()
    {
        Assert.False(AppLanguage.NeedsRestart("fr-BE", "en-US", inForce: "fr"));
    }

}
