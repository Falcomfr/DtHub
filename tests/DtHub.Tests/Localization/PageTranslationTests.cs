using DtHub.Core.Localization;

namespace DtHub.Tests.Localization;

/// <summary>
/// The Translate button: offered only on a page written in another
/// language than the application's, and aimed at the application's.
/// </summary>
public sealed class PageTranslationTests
{
    [Theory]
    [InlineData("fr-FR", "en")]
    [InlineData("fr", "es")]
    [InlineData("en", "fr")]
    public void Une_page_dans_une_autre_langue_propose_la_traduction(string page, string app)
        => Assert.True(PageTranslation.Offers(page, app));

    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("FR", "fr")]
    [InlineData("en-US", "en")]
    [InlineData("es_ES", "es")]
    public void Une_page_dans_la_langue_de_l_application_ne_propose_rien(string page, string app)
        => Assert.False(PageTranslation.Offers(page, app));

    /// <summary>
    /// A page that does not say its language: guessing would put a button
    /// on a page already readable.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Une_page_sans_langue_declaree_ne_propose_rien(string? page)
        => Assert.False(PageTranslation.Offers(page, "en"));

    [Fact]
    public void L_adresse_traduit_vers_la_langue_de_l_application()
        => Assert.Equal(
            "https://translate.google.com/translate?sl=auto&tl=es&u=https%3A%2F%2Fpapycha.fr%2Fla-quete%2F%3Fa%3D1%26b%3D2",
            PageTranslation.Address("https://papycha.fr/la-quete/?a=1&b=2", "es"));
}
