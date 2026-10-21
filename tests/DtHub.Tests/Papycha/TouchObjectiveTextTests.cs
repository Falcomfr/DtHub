using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class TouchObjectiveTextTests
{
    private static string? Name(TouchHole kind, int id) => (kind, id) switch
    {
        (TouchHole.Npc, 447) or (TouchHole.Npc, 119) => "Invented NPC",
        (TouchHole.Item, 1746) => "Invented item",
        (TouchHole.Text, 662825) => "Free text",
        _ => null,
    };

    [Fact]
    public void Aller_voir_prend_le_nom_du_PNJ() =>
        Assert.Equal("Talk to Invented NPC", TouchObjectiveText.Fill("Talk to #1", 1, [447], Name));

    [Fact]
    public void Ramener_des_objets_remplit_les_trois_trous() =>
        Assert.Equal(
            "Take 2 Invented item to Invented NPC",
            TouchObjectiveText.Fill("Take #3 #2 to #1", 3, [119, 1746, 2], Name));

    [Fact]
    public void Un_texte_libre_vient_des_textes() =>
        Assert.Equal("Free text", TouchObjectiveText.Fill("#1", 0, [662825], Name));

    [Fact]
    public void Un_nom_manquant_laisse_le_numero() =>
        Assert.Equal("Talk to 5", TouchObjectiveText.Fill("Talk to #1", 1, [5], Name));
}
