using System.Globalization;
using System.Text;

namespace DtHub.Core.Diagnostics;

/// <summary>
/// What we know about the machine and the session, for a report.
/// </summary>
/// <param name="Product">Product name and version.</param>
/// <param name="System">Windows and its version number.</param>
/// <param name="Runtime">The execution runtime.</param>
/// <param name="Culture">Interface language and format region.</param>
/// <param name="Screens">
/// The screens, as the launcher already summarizes them.
/// </param>
/// <param name="Devices">
/// The recognized devices, without their identity.
/// </param>
/// <param name="Sessions">
/// How many accounts are open, and how many in tabs.
/// </param>
public readonly record struct DiagnosticFacts(
    string Product,
    string System,
    string Runtime,
    string Culture,
    string Screens,
    string Devices,
    string Sessions);

/// <summary>
/// Composes the text a person pastes into a bug report.
///
/// It fits on one page and reads without any tool: what failed, on
/// which machine, and the log lines around the fault. Everything
/// goes through <see cref="Redaction"/> before being rendered,
/// including what the application believes it knows about itself: an
/// exception message sometimes carries a path, and a stack trace
/// almost always does.
///
/// The text is in French, like the logs it draws its lines from.
/// Translating it would give a half-translated report, which would
/// help no one.
/// </summary>
public static class DiagnosticReport
{
    /// <summary>Composes the report.</summary>
    /// <param name="headline">What failed, in one line.</param>
    /// <param name="facts">The state of the machine.</param>
    /// <param name="error">The exception, if the fault produced one.</param>
    /// <param name="lastFailure">
    /// The last known technical refusal, if there is one.
    /// </param>
    /// <param name="log">The log lines already sorted.</param>
    /// <param name="secrets">What the application knows it must mask.</param>
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

    /// <summary>
    /// A line of facts, empty ones discarded, separated by a middle
    /// dot.
    /// </summary>
    private static void Line(StringBuilder text, params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();

        if (kept.Count > 0)
        {
            text.AppendLine(string.Join(" · ", kept));
        }
    }

    /// <summary>
    /// The URL for a new issue on the repository, with its title
    /// already set.
    ///
    /// The body is not included: a URL goes well beyond what a
    /// browser accepts past eight thousand characters, and a report
    /// alone is that long by itself. The body is pasted from the
    /// clipboard, and the repository's issue template says where.
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
