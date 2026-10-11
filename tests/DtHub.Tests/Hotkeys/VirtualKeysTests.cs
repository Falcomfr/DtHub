using DtHub.Core.Hotkeys;

namespace DtHub.Tests.Hotkeys;

public sealed class VirtualKeysTests
{
    [Theory]
    [InlineData(VirtualKeys.A)]
    [InlineData(VirtualKeys.D5)]
    [InlineData(VirtualKeys.NumPad0)]
    [InlineData(VirtualKeys.Space)]
    [InlineData(VirtualKeys.Enter)]
    [InlineData(VirtualKeys.Back)]
    [InlineData(0xBC)] // comma
    public void Une_touche_d_ecriture_sert_au_chat_du_jeu(int key) =>
        Assert.True(VirtualKeys.IsTypingKey(key));

    [Theory]
    [InlineData(VirtualKeys.Tab)]
    [InlineData(VirtualKeys.F1)]
    [InlineData(VirtualKeys.Left)]
    [InlineData(VirtualKeys.PageUp)]
    [InlineData(VirtualKeys.Escape)]
    public void Les_autres_touches_ne_servent_pas_a_ecrire(int key) =>
        Assert.False(VirtualKeys.IsTypingKey(key));
}
