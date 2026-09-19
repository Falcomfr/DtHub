namespace DtHub.Core.Settings;

/// <summary>
/// Which colour an account gets, and in what order.
///
/// Pure, so it can be tested without a phone, a file or a window.
/// </summary>
public static class AccountColours
{
    /// <summary>
    /// Every colour, in the order the picker shows them.
    ///
    /// <see cref="AccountColour.None" /> is not one of them: it is the
    /// absence of a mark, not a mark, so it is neither offered nor
    /// counted as occupying a slot.
    /// </summary>
    public static readonly AccountColour[] All =
        [.. Enum.GetValues<AccountColour>().Where(c => c != AccountColour.None)];

    /// <summary>
    /// The order colours are handed out in, which is not the order they
    /// are declared in.
    ///
    /// Declaration order runs round the wheel, so the first two accounts
    /// would get two neighbouring hues, and two accounts is the common
    /// case. This order gives the first three the three widest-apart
    /// colours of the set, and it never hands out Indigo and Orchid one
    /// after the other, which are the closest pair.
    /// </summary>
    public static readonly AccountColour[] AssignmentOrder =
    [
        AccountColour.Teal,
        AccountColour.Brass,
        AccountColour.Indigo,
        AccountColour.Moss,
        AccountColour.Rose,
        AccountColour.Orchid,
    ];

    /// <summary>
    /// The first colour nobody is using, or <c>null</c> when all six are
    /// taken.
    ///
    /// **The seventh account gets no colour, and does not wrap around.**
    /// Two accounts sharing a colour is not a smaller version of the
    /// feature, it is the feature lying: someone who trusts the colour
    /// and acts on the wrong window is worse off than someone who knows
    /// the seventh is unmarked. They can still choose a duplicate by
    /// hand; the difference is that the application never does it behind
    /// their back.
    /// </summary>
    public static AccountColour? NextFree(IEnumerable<AccountColour?> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);

        var used = taken
            .Where(c => c is not null and not AccountColour.None)
            .Select(c => c!.Value)
            .ToList();

        foreach (var colour in AssignmentOrder)
        {
            if (!used.Contains(colour))
            {
                return colour;
            }
        }

        return null;
    }
}
