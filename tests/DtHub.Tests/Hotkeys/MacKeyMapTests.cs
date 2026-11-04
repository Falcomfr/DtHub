using DtHub.Core.Hotkeys;
using DtHub.Mac.Platform;

namespace DtHub.Tests.Hotkeys;

/// <summary>
/// The Mac's translation of the PC's keys (src/DtHub.Mac/Platform/MacKeyMap.cs,
/// linked here). The layouts are given as the Mac reads them: an American
/// keyboard, and a French one where A and Q, Z and W swap places, M stands
/// where ";" does and "," where M does.
/// </summary>
public sealed class MacKeyMapTests
{
    private static readonly Dictionary<int, char> Qwerty = new()
    {
        [0x00] = 'a',
        [0x0C] = 'q',
        [0x0D] = 'w',
        [0x06] = 'z',
        [0x29] = ';',
        [0x2E] = 'm',
        [0x23] = 'p',
        [0x12] = '1',
    };

    private static readonly Dictionary<int, char> Azerty = new()
    {
        [0x00] = 'q',
        [0x0C] = 'a',
        [0x0D] = 'z',
        [0x06] = 'w',
        [0x29] = 'm',
        [0x2E] = ',',
        [0x23] = 'p',
        [0x12] = '&',
    };

    [Theory]
    [InlineData(VirtualKeys.P, 0x23)]
    [InlineData(VirtualKeys.A, 0x0C)]
    [InlineData(VirtualKeys.Q, 0x00)]
    [InlineData(VirtualKeys.M, 0x29)]
    [InlineData(VirtualKeys.D1, 0x12)]
    [InlineData(VirtualKeys.Tab, 0x30)]
    [InlineData(VirtualKeys.F1, 0x7A)]
    [InlineData(VirtualKeys.F5, 0x60)]
    [InlineData(VirtualKeys.Escape, 0x35)]
    [InlineData(0xBC, 0x2E)] // the comma, where an American keyboard has M
    public void Une_lettre_suit_la_disposition_le_reste_la_place(int virtualKey, int expected) =>
        Assert.Equal(expected, MacKeyMap.ToKeyCode(virtualKey, Typing(Azerty)));

    [Fact]
    public void Sur_un_clavier_americain_les_lettres_restent_a_leur_place()
    {
        Assert.Equal(0x00, MacKeyMap.ToKeyCode(VirtualKeys.A, Typing(Qwerty)));
        Assert.Equal(0x2E, MacKeyMap.ToKeyCode(VirtualKeys.M, Typing(Qwerty)));
    }

    [Fact]
    public void Une_lettre_absente_de_la_disposition_retombe_sur_sa_place() =>
        Assert.Equal(0x08, MacKeyMap.ToKeyCode(VirtualKeys.C, _ => null));

    [Theory]
    [InlineData(VirtualKeys.Pause)]
    [InlineData(0x87)] // F24
    public void Une_touche_que_le_Mac_n_a_pas_n_a_pas_de_code(int virtualKey) =>
        Assert.Null(MacKeyMap.ToKeyCode(virtualKey, _ => null));

    [Theory]
    [InlineData(0x0C, VirtualKeys.A)]
    [InlineData(0x29, VirtualKeys.M)]
    [InlineData(0x12, VirtualKeys.D1)]
    [InlineData(0x2E, 0xBC)]
    [InlineData(0x7A, VirtualKeys.F1)]
    [InlineData(0x30, VirtualKeys.Tab)]
    public void Une_touche_tapee_redonne_la_touche_du_PC(int keyCode, int expected) =>
        Assert.Equal(expected, MacKeyMap.ToVirtualKey(keyCode, Character(Azerty)));

    [Fact]
    public void Chaque_raccourci_par_defaut_fait_l_aller_retour()
    {
        foreach (var layout in new[] { Qwerty, Azerty })
        {
            foreach (var binding in HotkeySet.Default.Bindings.Where(b => b.IsAssigned))
            {
                var code = MacKeyMap.ToKeyCode(binding.VirtualKey, Typing(layout));

                Assert.NotNull(code);
                Assert.Equal(binding.VirtualKey, MacKeyMap.ToVirtualKey(code.Value, Character(layout)));
            }
        }
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, 0u)]
    [InlineData(HotkeyModifiers.Control, MacKeyMap.ControlKey)]
    [InlineData(HotkeyModifiers.Alt, MacKeyMap.OptionKey)]
    [InlineData(HotkeyModifiers.Windows, MacKeyMap.CmdKey)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift, MacKeyMap.ControlKey | MacKeyMap.ShiftKey)]
    public void Ctrl_reste_Control_et_Windows_devient_Commande(HotkeyModifiers modifiers, uint expected) =>
        Assert.Equal(expected, MacKeyMap.ToCarbonModifiers(modifiers));

    private static Func<char, int?> Typing(Dictionary<int, char> layout) =>
        letter => layout.Where(pair => pair.Value == letter).Select(pair => (int?)pair.Key).FirstOrDefault();

    private static Func<int, char?> Character(Dictionary<int, char> layout) =>
        code => layout.TryGetValue(code, out var typed) ? typed : null;
}
