using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Ce qu'un intitulé de prérequis désigne vraiment.
///
/// Le site en écrit de trois formes, et deux d'entre elles ne sont pas des
/// titres de quête. Un jalon n'est pas une quête mais l'état qu'elle laisse :
/// « L'essentiel est dans le Lac gelé atteint ». Et un succès entier se cite
/// « Succès Un nouveau départ réalisé ». Relevé sur les 584 prérequis du
/// catalogue : 511 titres nus, 35 jalons, 38 succès. Les crochets d'autrefois,
/// « [FIN] … », ont disparu du site mais la règle reste, le catalogue embarqué
/// pouvant dater d'avant.
///
/// Les rapprocher tels quels laissait donc **un prérequis sur huit** sans
/// suite : c'est ainsi que « La légende du Chevalier de l'Automne » n'avait
/// aucune quête suivante, la seule arête qui mène au succès d'après étant
/// portée par un libellé de succès.
///
/// La règle est la même que celle du script d'extraction,
/// <c>build/extract-successes.py</c>, fonction <c>sans_marque</c>. Elle vivait
/// jusqu'ici du seul côté Python, qui ne recopiait pas son résultat dans le
/// fichier : l'application ne pouvait pas la connaître.
/// </summary>
public static partial class PrerequisiteLabel
{
    /// <summary>Ce qu'un intitulé de prérequis nomme.</summary>
    /// <param name="Name">Le nom, marques retirées.</param>
    /// <param name="IsSuccess">Vrai quand c'est un succès entier, non une quête.</param>
    public readonly record struct Target(string Name, bool IsSuccess);

    /// <summary>Lit un intitulé de prérequis.</summary>
    public static Target Of(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return new Target(string.Empty, false);
        }

        var value = BracketPattern().Replace(label, string.Empty).Trim();

        if (SuccessPattern().Match(value) is { Success: true } success)
        {
            return new Target(success.Groups["name"].Value.Trim(), true);
        }

        return new Target(ReachedPattern().Replace(value, string.Empty).Trim(), false);
    }

    [GeneratedRegex(@"^\[[^\]]*\]\s*", RegexOptions.None, 250)]
    private static partial Regex BracketPattern();

    [GeneratedRegex(
        @"^Succ[èe]s\s+(?<name>.+?)\s+r[ée]alis[ée]e?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex SuccessPattern();

    [GeneratedRegex(
        @"\s+atteint(?:e)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex ReachedPattern();
}
