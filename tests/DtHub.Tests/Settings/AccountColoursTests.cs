using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class AccountColoursTests
{
    /// <summary>
    /// The handing-out order must offer every colour exactly once. A
    /// duplicate would make one colour unreachable and hand another out
    /// twice, and nothing else in the application would notice.
    /// </summary>
    [Fact]
    public void L_ordre_d_attribution_couvre_toutes_les_couleurs_une_fois()
    {
        Assert.Equal(
            AccountColours.All.OrderBy(c => c),
            AccountColours.AssignmentOrder.OrderBy(c => c));

        Assert.Equal(
            AccountColours.AssignmentOrder.Length,
            AccountColours.AssignmentOrder.Distinct().Count());
    }

    [Fact]
    public void Un_premier_compte_recoit_la_premiere_couleur_de_l_ordre()
    {
        Assert.Equal(AccountColours.AssignmentOrder[0], AccountColours.NextFree([]));
    }

    [Fact]
    public void Une_couleur_deja_prise_est_passee()
    {
        Assert.Equal(
            AccountColours.AssignmentOrder[1],
            AccountColours.NextFree([AccountColours.AssignmentOrder[0]]));
    }

    /// <summary>
    /// Holes are filled rather than skipped: freeing a colour makes it
    /// available again, which is what lets someone clear one and give it
    /// to another account.
    /// </summary>
    [Fact]
    public void Une_couleur_rendue_redevient_disponible()
    {
        AccountColour?[] taken = [AccountColours.AssignmentOrder[1], null, AccountColours.AssignmentOrder[2]];

        Assert.Equal(AccountColours.AssignmentOrder[0], AccountColours.NextFree(taken));
    }

    /// <summary>
    /// The seventh account gets nothing rather than a duplicate. Two
    /// accounts sharing a colour is not a smaller version of the
    /// feature, it is the feature lying, and acting on the wrong window
    /// is worse than knowing one account is unmarked.
    /// </summary>
    [Fact]
    public void Le_septieme_compte_n_a_pas_de_couleur()
    {
        Assert.Null(AccountColours.NextFree(AccountColours.All.Select(c => (AccountColour?)c)));
    }

    /// <summary>
    /// The first three accounts, which is the common case, must not land
    /// on the two closest hues one after the other.
    /// </summary>
    [Fact]
    public void Indigo_et_Orchid_ne_se_suivent_jamais()
    {
        var order = AccountColours.AssignmentOrder;

        for (var position = 0; position < order.Length - 1; position++)
        {
            var pair = new[] { order[position], order[position + 1] };

            Assert.False(
                pair.Contains(AccountColour.Indigo) && pair.Contains(AccountColour.Orchid));
        }
    }
}
