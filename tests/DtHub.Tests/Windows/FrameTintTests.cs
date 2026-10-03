using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

public class FrameTintTests
{
    [Fact]
    public void Le_lisere_garde_moins_de_la_moitie_de_la_teinte_sur_le_fond_sombre()
    {
        // 2026-10-03: the full colour around a window caught the eye, "rendre
        // le liseré moins visible, plus discret", and a little over half of it
        // was still "à peine trop vif".
        // The vivid orchid #C66BDA, written as Windows wants it, 0x00BBGGRR.
        const int Orchidee = 0x00DA6BC6;

        // 0x20 + 0.45 x (channel - 0x20), per channel: #6B4274.
        Assert.Equal(0x0074426B, FrameTint.Border(Orchidee));
    }

    [Fact]
    public void Le_fond_sombre_reste_tel_quel()
    {
        Assert.Equal(0x00202020, FrameTint.Border(0x00202020));
    }
}
