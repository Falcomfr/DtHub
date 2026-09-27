using System.Globalization;

using DtHub.Core.Adb;
using DtHub.Core.Guidance;
using DtHub.Core.Localization;

namespace DtHub.Tests.Guidance;

public class OfflineAdviceTests
{
    /// <summary>The three embedded languages.</summary>
    private static readonly string[] Langues = ["en", "fr", "es"];

    private static IEnumerable<string> Toutes(string cle) =>
        Langues.Select(l => Strings.GetIn(cle, CultureInfo.GetCultureInfo(l)));

    [Fact]
    public void Un_telephone_au_bout_d_un_cable_ne_s_entend_jamais_parler_de_wifi()
    {
        // This is the whole reason for splitting the advice. The
        // unreachable phone used to be told, whatever it was attached
        // by, to check that it was "on the same Wi-Fi network as this
        // PC". On a cable that sends its owner looking at a network
        // which has nothing to do with the failure, and the likeliest
        // cause, a charge-only cable, is never even named.
        foreach (var cle in new[]
        {
            OfflineAdvice.HowKey(AdbConnectionKind.Usb),
            OfflineAdvice.TipKey(AdbConnectionKind.Usb),
        })
        {
            foreach (var texte in Toutes(cle))
            {
                Assert.DoesNotContain("Wi-Fi", texte, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Le_conseil_sans_fil_parle_bien_du_wifi()
    {
        foreach (var cle in new[]
        {
            OfflineAdvice.HowKey(AdbConnectionKind.Wireless),
            OfflineAdvice.TipKey(AdbConnectionKind.Wireless),
        })
        {
            foreach (var texte in Toutes(cle))
            {
                Assert.Contains("Wi-Fi", texte, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Chaque_maniere_d_etre_relie_a_ses_propres_mots()
    {
        string[] lignes =
        [
            OfflineAdvice.HowKey(AdbConnectionKind.Usb),
            OfflineAdvice.HowKey(AdbConnectionKind.Wireless),
            OfflineAdvice.HowKey(AdbConnectionKind.Unknown),
        ];

        Assert.Equal(lignes.Length, lignes.Distinct(StringComparer.Ordinal).Count());

        string[] bulles =
        [
            OfflineAdvice.TipKey(AdbConnectionKind.Usb),
            OfflineAdvice.TipKey(AdbConnectionKind.Wireless),
            OfflineAdvice.TipKey(AdbConnectionKind.Unknown),
        ];

        Assert.Equal(bulles.Length, bulles.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(AdbConnectionKind.Unknown)]
    [InlineData(AdbConnectionKind.Emulator)]
    public void Ce_qu_on_ne_sait_pas_relie_ne_promet_ni_cable_ni_reseau(AdbConnectionKind connexion)
    {
        // A phone whose serial says nothing, and the emulator, which is
        // outside the scope but detected cleanly: neither deserves an
        // assured answer. The neutral wording is the honest one, and it
        // is the same for both.
        Assert.Equal(
            OfflineAdvice.HowKey(AdbConnectionKind.Unknown),
            OfflineAdvice.HowKey(connexion),
            StringComparer.Ordinal);

        foreach (var texte in Toutes(OfflineAdvice.HowKey(connexion)))
        {
            Assert.DoesNotContain("Wi-Fi", texte, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Les_textes_existent_dans_les_trois_langues()
    {
        // Strings.GetIn hands back the key itself when it is missing,
        // which would show "OfflineHowUsb" on the line under a phone.
        foreach (var connexion in new[]
        {
            AdbConnectionKind.Usb, AdbConnectionKind.Wireless, AdbConnectionKind.Unknown,
        })
        {
            foreach (var cle in new[]
            {
                OfflineAdvice.HowKey(connexion), OfflineAdvice.TipKey(connexion),
            })
            {
                foreach (var texte in Toutes(cle))
                {
                    Assert.NotEqual(cle, texte, StringComparer.Ordinal);
                }
            }
        }
    }
}
