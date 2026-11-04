using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Tests.Localization;

/// <summary>
/// The Mac's text variants: used on macOS only, each one standing for a key
/// that exists, with the same placeholders, so that switching platform
/// never shows a key name or drops a value.
/// </summary>
public sealed partial class MacTextTests
{
    private static readonly ResourceManager Resources =
        new("DtHub.Core.Localization.Strings", typeof(Strings).Assembly);

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    [InlineData("es")]
    public void Les_consignes_propres_a_Windows_suivent_la_plateforme(string language)
    {
        var reserved = Strings.GetIn("HotkeyReserved", CultureInfo.GetCultureInfo(language));

        Assert.Contains(OperatingSystem.IsMacOS() ? "macOS" : "Windows", reserved, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_PC_devient_le_Mac_sur_Mac_seulement()
    {
        var text = Strings.GetIn("ForgetDeviceTip", CultureInfo.GetCultureInfo("fr"));

        Assert.Contains(OperatingSystem.IsMacOS() ? "ce Mac" : "ce PC", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    [InlineData("es")]
    public void Chaque_texte_Mac_double_une_cle_avec_les_memes_parametres(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var set = Resources.GetResourceSet(culture, true, true)!;
        var variants = 0;

        foreach (DictionaryEntry entry in set)
        {
            var key = (string)entry.Key;

            if (!key.StartsWith("MacText_", StringComparison.Ordinal))
            {
                continue;
            }

            variants++;
            var original = Resources.GetString(key["MacText_".Length..], culture);

            Assert.NotNull(original);
            Assert.Equal(Placeholders(original), Placeholders((string)entry.Value!));
        }

        Assert.True(variants > 0);
    }

    private static string[] Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
