using System.Text.RegularExpressions;

namespace DtHub.Tests.Conventions;

/// <summary>
/// A named style must target the type of the element that carries it.
///
/// WPF only checks this at runtime, and it does not forgive: applying a
/// <c>Button</c> style to a <c>ToggleButton</c> throws the moment the
/// window is being built, and the application dies taking its stack with
/// it. This happened on a note toggle that had been given an icon style:
/// the account list emptied out and the process stopped on a stack
/// overflow, whose cause only showed up at the bottom of the log.
///
/// The build says nothing about it, and no test could say it either,
/// since tests do not reach the interface layer. This one therefore reads
/// the text of the files, the same way the em dash check does.
/// </summary>
public partial class StyleTargetTests
{
    [Fact]
    public void Chaque_style_nomme_vise_le_type_qui_le_porte()
    {
        var root = RepositoryRoot.Path();

        var targets = Targets(Path.Combine(root, "src", "DtHub.App", "Themes"));

        Assert.NotEmpty(targets);

        List<string> wrong = [];

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src", "DtHub.App"), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (Match applied in Applied().Matches(text))
            {
                var element = applied.Groups[1].Value;
                var key = applied.Groups[2].Value;

                // A style we cannot find is defined elsewhere, in the
                // window itself for instance: we judge only what we see.
                if (targets.TryGetValue(key, out var target)
                    && !string.Equals(element, target, StringComparison.Ordinal))
                {
                    wrong.Add($"{Path.GetFileName(file)} : <{element}> porte « {key} », qui vise {target}.");
                }
            }
        }

        Assert.Equal([], wrong);
    }

    /// <summary>
    /// A style written directly on the element must derive from the
    /// implicit style of its type, when one exists.
    ///
    /// **WPF replaces, it does not extend.** A
    /// <c>&lt;TextBlock.Style&gt;&lt;Style TargetType="TextBlock"&gt;</c>
    /// without <c>BasedOn</c> discards the theme's implicit style, hence
    /// the font, the size, and above all the text color, which falls
    /// back to WPF's default black. On a dark background, the text
    /// disappears.
    ///
    /// This happened to the input probe's verdict: "L'appareil accepte la
    /// simulation d'entrée" ("The device accepts simulated input")
    /// displayed in black on the dark card, and only the refusal verdict
    /// showed, because only that one set a color in a trigger. Nothing
    /// flagged it, neither the build nor running the app.
    /// </summary>
    [Fact]
    public void Un_style_ecrit_sur_l_element_derive_du_style_implicite()
    {
        var root = RepositoryRoot.Path();
        var implicites = Implicit(Path.Combine(root, "src", "DtHub.App", "Themes"));

        Assert.NotEmpty(implicites);

        List<string> orphans = [];

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src", "DtHub.App"), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (Match inline in Inline().Matches(text))
            {
                var attributes = inline.Groups[2].Value;

                if (attributes.Contains("BasedOn", StringComparison.Ordinal))
                {
                    continue;
                }

                var target = TargetType().Match(attributes);

                if (target.Success && implicites.Contains(target.Groups[1].Value))
                {
                    orphans.Add(
                        $"{Path.GetFileName(file)} : <{inline.Groups[1].Value}.Style> "
                        + $"jette le style implicite de {target.Groups[1].Value}.");
                }
            }
        }

        Assert.Equal([], orphans);
    }

    /// <summary>
    /// The types the themes style without a key, so for every element.
    /// </summary>
    private static HashSet<string> Implicit(string themes)
    {
        HashSet<string> found = new(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(themes, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match declared in Anonymous().Matches(File.ReadAllText(file)))
            {
                found.Add(declared.Groups[1].Value);
            }
        }

        return found;
    }

    /// <summary>
    /// The named styles of the themes, and the type each one targets.
    /// </summary>
    private static Dictionary<string, string> Targets(string themes)
    {
        Dictionary<string, string> found = new(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(themes, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match declared in Declared().Matches(File.ReadAllText(file)))
            {
                found[declared.Groups[1].Value] = declared.Groups[2].Value;
            }
        }

        return found;
    }

    // Styles derived from another are not judged here: "BasedOn" already
    // requires type compatibility, and WPF checks it at compile time.
    [GeneratedRegex(@"<Style\s+x:Key=""(\w+)""\s+TargetType=""(\w+)""")]
    private static partial Regex Declared();

    [GeneratedRegex(@"<(\w+)\b[^>]*?Style=""\{StaticResource (\w+)\}""", RegexOptions.Singleline)]
    private static partial Regex Applied();

    /// <summary>
    /// A style declared without a key styles every element of the type.
    /// </summary>
    [GeneratedRegex(@"<Style\s+TargetType=""(\w+)""\s*(?:BasedOn=""[^""]*""\s*)?/?>")]
    private static partial Regex Anonymous();

    /// <summary>A style written inside the element itself.</summary>
    [GeneratedRegex(@"<(\w+)\.Style>\s*<Style([^>]*)>")]
    private static partial Regex Inline();

    [GeneratedRegex(@"TargetType=""(\w+)""")]
    private static partial Regex TargetType();
}
