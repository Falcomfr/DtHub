using System.Text.RegularExpressions;

namespace DtHub.Tests.App;

/// <summary>
/// The palette read as text, like the other checks that look at what is
/// written rather than at what runs.
///
/// The rules here are the ones a well-meaning tweak breaks silently: a
/// tint nudged onto the accent, or two of the six made equal, changes
/// nothing that builds and everything that the colours are for.
/// </summary>
public class PaletteTests
{
    private static readonly Dictionary<string, string> Palette = Read();

    private static Dictionary<string, string> Read()
    {
        var text = File.ReadAllText(Path.Combine(
            RepositoryRoot.Path(), "src", "DtHub.App", "Themes", "PaletteDark.xaml"));

        return Regex
            // The colour is a hexadecimal value for every brush but one:
            // the unlit socket is written "Transparent", by name.
            .Matches(text, """<SolidColorBrush x:Key="(\w+)" Color="([^"]+)" />""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());
    }

    private static readonly string[] Comptes =
        ["AccountTintA", "AccountTintB", "AccountTintC", "AccountTintD", "AccountTintE", "AccountTintF"];

    [Fact]
    public void Les_six_teintes_de_compte_existent()
    {
        foreach (var key in Comptes)
        {
            Assert.True(Palette.ContainsKey(key), key);
        }
    }

    [Fact]
    public void Les_six_teintes_de_compte_sont_distinctes()
    {
        var values = Comptes.Select(k => Palette[k]).ToList();

        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The accent is "the colour of what you select and what you touch,
    /// and it must remain the only one of its kind". The three status
    /// LEDs carry a meaning of their own, and the menu tints belong to
    /// the help artwork. An account colour must be none of them.
    /// </summary>
    [Fact]
    public void Aucune_teinte_de_compte_ne_reprend_une_couleur_reservee()
    {
        string[] reserved =
        [
            "AccentBrush", "AccentHoverBrush", "AccentSoftBrush",
            "SuccessBrush", "WarningBrush", "DangerBrush",
            "MenuTintA", "MenuTintB", "MenuTintC", "MenuTintD", "MenuTintE", "MenuTintF",
        ];

        var taken = reserved
            .Where(Palette.ContainsKey)
            .Select(k => Palette[k])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var key in Comptes)
        {
            Assert.DoesNotContain(Palette[key], taken, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The unlit socket has to resolve, or the key-to-brush converter
    /// falls back to grey and a row at rest claims to be in an unknown
    /// state rather than in no state at all.
    /// </summary>
    [Fact]
    public void Le_culot_eteint_est_declare()
    {
        Assert.True(Palette.ContainsKey("EmptySocketBrush"));
    }
}
