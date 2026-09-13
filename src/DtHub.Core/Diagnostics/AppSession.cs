namespace DtHub.Core.Diagnostics;

/// <summary>
/// The identifier of this launch, carried by every log line.
///
/// The template had none, and four hundred and eight startups
/// recorded over six days got mixed together in seven files. A
/// report that takes "the last lines" would then carry off the
/// previous day's errors, and one that takes "the errors in the
/// file" would carry off hundreds that were unrelated.
///
/// Six characters are enough: the identifier is only used to split a
/// single day's file, not to tell two machines apart. It reveals
/// nothing about anyone: it is drawn at random on each launch and
/// does not survive being closed.
/// </summary>
public static class AppSession
{
    /// <summary>Length of the identifier.</summary>
    public const int Length = 6;

    /// <summary>The identifier of this launch.</summary>
    public static string Id { get; } = Mint();

    private static string Mint()
    {
        // Without any ambiguous letter or vowel: an identifier that
        // gets read back in a log must neither be mistaken for a
        // word nor be misread.
        const string alphabet = "0123456789bcdfghjkmnpqrstvwxz";

        return string.Create(
            Length,
            alphabet,
            (span, source) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = source[Random.Shared.Next(source.Length)];
                }
            });
    }
}
