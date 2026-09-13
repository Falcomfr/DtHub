using System.Globalization;

using DtHub.Core.Localization;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Tests.Packaging;

/// <summary>
/// What the shipped file must contain beyond its code.
///
/// Nothing used to fail if one of these resources left a csproj: the
/// gap only showed up at runtime, for the person who had downloaded
/// the application. A line removed by mistake during a refactor would
/// not have been noticed anywhere before that.
/// </summary>
public class EmbeddedResourceTests
{
    private static readonly string[] Langues = ["en", "fr", "es"];

    /// <summary>
    /// The names are the ones the code asks for, not the ones on disk.
    /// </summary>
    [Theory]
    [InlineData("DtHub.Infrastructure.dependencies.json")]
    [InlineData("DtHub.Infrastructure.quest-successes.json")]
    public void La_ressource_est_embarquee_et_se_lit(string name)
    {
        var assembly = typeof(DependencyManifest).Assembly;

        Assert.Contains(name, assembly.GetManifestResourceNames());

        using var stream = assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        Assert.True(stream.Length > 0, $"« {name} » est embarquée mais vide.");
    }

    [Fact]
    public void Les_deux_dependances_sont_declarees()
    {
        // The manifest is read from the resource: this test also
        // fails if the file is present but unreadable.
        Assert.Equal(2, DependencyManifest.All.Count);
        Assert.NotNull(DependencyManifest.Get(DependencyManifest.PlatformToolsKey));
        Assert.NotNull(DependencyManifest.Get(DependencyManifest.ScrcpyKey));
    }

    /// <summary>
    /// The translation satellites. The csproj itself warns that a
    /// misconfigured <c>SatelliteResourceLanguages</c> would drop
    /// them without saying a word: three distinct texts for the same
    /// key prove it.
    /// </summary>
    [Fact]
    public void Les_trois_langues_rendent_trois_textes()
    {
        const string Key = "PreparationTitle";

        var textes = Langues
            .Select(langue => Strings.GetIn(Key, CultureInfo.GetCultureInfo(langue)))
            .ToList();

        Assert.DoesNotContain(Key, textes);
        Assert.Equal(3, textes.Distinct(StringComparer.Ordinal).Count());
    }
}
