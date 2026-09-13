using System.Text.RegularExpressions;

namespace DtHub.Tests.Diagnostics;

/// <summary>
/// A caught error must resurface: through the log, through the screen,
/// through a failure returned to the caller, or through a comment that
/// says why silence is the right choice.
///
/// The convention is already written in AGENTS.md, "no silent catch
/// (Exception)". A survey found it broken thirty-one times, eighteen of
/// them for real: the other thirteen did surface the error, but in a way
/// the survey did not recognize. That is why this test enumerates the
/// ways to surface an error rather than looking for a single word.
///
/// The check is done on the text of the files, like the one for XAML
/// commands: otherwise it would have to reference the interface project
/// and switch the whole suite over to Windows.
/// </summary>
public sealed class CatchDisciplineTests
{
    private static readonly Regex Opens = new(@"^\s*catch\b");

    /// <summary>
    /// The ways an error can surface from a block. Logging it, showing
    /// it, returning it as a named failure, or rethrowing it.
    /// </summary>
    private static readonly Regex Speaks = new(
        @"Log[A-Za-z]*\(|Log\.\w+\(|_logger|Report\(|throw|"
        + @"Problem\s*=|Status\s*=|FailureKind\s*=|FailureMessage\s*=|"
        + @"Show(Warning|Information|Error)|"
        + @"FailedSession|AppLaunchResult\.Failure|AccountAddition\(|"
        + @"\.Record\(|TrySetException|ThrowIfCancellationRequested|ReportViewFailure|"
        + @"Note\(");

    [Fact]
    public void Aucune_erreur_n_est_avalee_sans_un_mot()
    {
        List<string> muets = [];

        foreach (var (file, lines) in Sources())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Opens.IsMatch(lines[i]) || Speaks.IsMatch(Body(lines, i)))
                {
                    continue;
                }

                // A comment, inside the block or right above it, counts
                // as an owned decision: that is what the convention asks
                // for.
                var above = string.Join('\n', lines[Math.Max(0, i - 4)..i]);

                if (!Body(lines, i).Contains("//", StringComparison.Ordinal)
                    && !above.Contains("//", StringComparison.Ordinal))
                {
                    muets.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.Equal([], muets.Order());
    }

    /// <summary>
    /// The sentence that declares a deliberate exemption, under the
    /// block.
    /// </summary>
    private const string Derogation = "No filter, and that is intentional";

    /// <summary>
    /// Catching every exception with no filter hides the faults we do
    /// not want to handle, starting with running out of memory.
    ///
    /// The exemption is no longer declared by a pair of coordinates, but
    /// by a sentence written under the block. The test used to assert
    /// "Win32HotkeyRegistrar.cs:228": inserting a line anywhere else in
    /// that file would make it fail even though it had nothing to do
    /// with what had been changed. Anchored on the sentence instead, it
    /// also becomes stronger: a new filterless block can only escape it
    /// by writing that same deliberate decision, at the place where it
    /// will be read.
    /// </summary>
    [Fact]
    public void Attraper_tout_se_borne_par_un_filtre()
    {
        List<string> larges = [];

        foreach (var (file, lines) in Sources())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s*catch\s*\(\s*Exception\b"))
                {
                    continue;
                }

                // The filter is sometimes written on the following line.
                var window = lines[i] + " " + (i + 1 < lines.Length ? lines[i + 1] : string.Empty);

                if (window.Contains(" when ", StringComparison.Ordinal)
                    || Body(lines, i).Contains(Derogation, StringComparison.Ordinal))
                {
                    continue;
                }

                larges.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        Assert.Equal([], larges.Order());
    }

    /// <summary>The body of the block, up to its closing brace.</summary>
    private static string Body(string[] lines, int at)
    {
        var indent = lines[at].Length - lines[at].TrimStart().Length;
        List<string> body = [];

        for (var i = at + 1; i < Math.Min(lines.Length, at + 16); i++)
        {
            if (lines[i].Trim() == "}" && lines[i].Length - lines[i].TrimStart().Length == indent)
            {
                break;
            }

            body.Add(lines[i]);
        }

        return string.Join('\n', body);
    }

    private static IEnumerable<(string File, string[] Lines)> Sources()
    {
        var root = Path.Combine(RepositoryRoot.Path(), "src");

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            yield return (file, File.ReadAllLines(file));
        }
    }

}
