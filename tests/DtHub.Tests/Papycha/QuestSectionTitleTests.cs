using System.Globalization;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestSectionTitleTests : IDisposable
{
    public void Dispose()
    {
        Strings.Speak(null);
        GC.SuppressFinalize(this);
    }

    private static readonly QuestSection Frigost = new() { Id = 135, Name = "Île de Frigost" };

    [Fact]
    public void En_anglais_Frigost_s_appelle_Frigost_Island()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Frigost Island", QuestSectionTitle.Of(Frigost));
    }

    [Fact]
    public void En_francais_le_titre_reste_celui_du_site()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("fr"));

        Assert.Equal("Île de Frigost", QuestSectionTitle.Of(Frigost));
        Assert.Equal("Bworks", QuestSectionTitle.Of(new QuestSection { Id = -5, Name = "Quêtes des Bworks" }));
    }

    [Fact]
    public void Une_rubrique_inconnue_garde_son_nom_francais()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("es"));

        Assert.Equal("Port de Madrestam", QuestSectionTitle.Of(new QuestSection { Id = 9999, Name = "Quêtes du Port de Madrestam" }));
    }
}
