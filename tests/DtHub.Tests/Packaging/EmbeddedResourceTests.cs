using System.Globalization;

using DtHub.Core.Localization;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Tests.Packaging;

/// <summary>
/// Ce que le fichier livré doit contenir en plus de son code.
///
/// Rien n'échouait si l'une de ces ressources quittait un csproj : le manque ne
/// se voyait qu'à l'exécution, chez la personne qui avait téléchargé
/// l'application. Une ligne retirée par mégarde pendant un remaniement ne se
/// serait vue nulle part avant.
/// </summary>
public class EmbeddedResourceTests
{
    private static readonly string[] Langues = ["en", "fr", "es"];

    /// <summary>Les noms sont ceux que le code demande, pas ceux du disque.</summary>
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
        // Le manifeste est lu depuis la ressource : cette épreuve échoue aussi
        // si le fichier est présent mais illisible.
        Assert.Equal(2, DependencyManifest.All.Count);
        Assert.NotNull(DependencyManifest.Get(DependencyManifest.PlatformToolsKey));
        Assert.NotNull(DependencyManifest.Get(DependencyManifest.ScrcpyKey));
    }

    /// <summary>
    /// Les satellites de traduction. Le csproj avertit lui-même qu'un
    /// <c>SatelliteResourceLanguages</c> mal réglé les supprimerait « sans rien
    /// dire » : trois textes distincts pour une même clé le disent.
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
