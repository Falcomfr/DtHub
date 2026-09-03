using System.Globalization;
using System.Text;

namespace DtHub.Core.Diagnostics;

/// <summary>Ce qu'on sait de la machine et de la session, pour un rapport.</summary>
/// <param name="Product">Nom et version du produit.</param>
/// <param name="System">Windows et son numéro de version.</param>
/// <param name="Runtime">La plateforme d'exécution.</param>
/// <param name="Culture">Langue de l'interface et pays des formats.</param>
/// <param name="Screens">Les écrans, tels que le lanceur les résume déjà.</param>
/// <param name="Devices">Les appareils reconnus, sans leur identité.</param>
/// <param name="Sessions">Combien de comptes sont ouverts, et combien en onglets.</param>
public readonly record struct DiagnosticFacts(
    string Product,
    string System,
    string Runtime,
    string Culture,
    string Screens,
    string Devices,
    string Sessions);

/// <summary>
/// Compose le texte qu'une personne colle dans un signalement.
///
/// Il tient en une page et se lit sans outil : ce qui a échoué, sur quelle
/// machine, et les lignes de journal qui entourent la faute. Tout y passe par
/// <see cref="Redaction"/> avant d'être rendu, y compris ce que l'application
/// croit connaître d'elle-même : un message d'exception porte parfois un chemin,
/// et une pile d'appel presque toujours.
///
/// Le texte est en français, comme les journaux dont il tire ses lignes. Le
/// traduire donnerait un rapport à moitié traduit, ce qui n'aiderait personne.
/// </summary>
public static class DiagnosticReport
{
    /// <summary>Compose le rapport.</summary>
    /// <param name="headline">Ce qui a échoué, en une ligne.</param>
    /// <param name="facts">L'état de la machine.</param>
    /// <param name="error">L'exception, si la faute en a produit une.</param>
    /// <param name="lastFailure">Le dernier refus technique connu, s'il y en a un.</param>
    /// <param name="log">Les lignes de journal déjà triées.</param>
    /// <param name="secrets">Ce que l'application sait devoir masquer.</param>
    public static string Compose(
        string? headline,
        DiagnosticFacts facts,
        Exception? error = null,
        string? lastFailure = null,
        string? log = null,
        IEnumerable<string>? secrets = null)
    {
        List<string> retire = [.. secrets ?? []];

        var text = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(headline))
        {
            text.Append(headline!.Trim()).Append("\n\n");
        }

        Line(text, facts.Product, facts.System, facts.Runtime, facts.Culture);
        Line(text, facts.Screens);
        Line(text, facts.Devices, facts.Sessions);

        if (error is not null)
        {
            text.Append('\n')
                .Append(error.GetType().Name)
                .Append(" : ")
                .Append(error.Message)
                .Append('\n');

            if (error.StackTrace is { Length: > 0 } pile)
            {
                text.Append(pile).Append('\n');
            }

            if (error.InnerException is { } dessous)
            {
                text.Append("Cause : ")
                    .Append(dessous.GetType().Name)
                    .Append(" : ")
                    .Append(dessous.Message)
                    .Append('\n');
            }
        }

        if (!string.IsNullOrWhiteSpace(lastFailure))
        {
            text.Append("\nDernier refus technique :\n").Append(lastFailure!.Trim()).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(log))
        {
            text.Append("\nJournal de cette session :\n").Append(log!.Trim()).Append('\n');
        }

        return Redaction.Apply(text.ToString().TrimEnd('\n'), retire);
    }

    /// <summary>Une ligne de faits, les vides écartés, séparés par un point médian.</summary>
    private static void Line(StringBuilder text, params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();

        if (kept.Count > 0)
        {
            text.AppendLine(string.Join(" · ", kept));
        }
    }

    /// <summary>
    /// L'adresse d'un signalement neuf sur le dépôt, avec son titre déjà posé.
    ///
    /// Le corps n'y est pas : une adresse dépasse ce qu'un navigateur accepte
    /// bien au-delà de huit mille caractères, et un rapport en fait autant à lui
    /// seul. Le corps se colle depuis le presse-papiers, et le modèle
    /// d'incident du dépôt dit où.
    /// </summary>
    public static string IssueUrl(string repositoryUrl, string? headline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);

        var title = string.IsNullOrWhiteSpace(headline)
            ? string.Empty
            : "&title=" + Uri.EscapeDataString(Cut(headline!.Trim(), 120));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{repositoryUrl.TrimEnd('/')}/issues/new?template=bug.yml{title}");
    }

    private static string Cut(string value, int most) =>
        value.Length <= most ? value : value[..most].TrimEnd() + "…";
}
