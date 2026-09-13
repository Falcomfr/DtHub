namespace DtHub.Core.Android;

/// <summary>
/// What must be done to a text before handing it to the phone's
/// shell.
///
/// ADB does not pass arguments one by one: it glues them back
/// together with spaces and lets the device's shell split them apart
/// again. A name that contains a space therefore arrives in two
/// pieces, and only the first one counts. Measured:
/// <c>pm create-user Compte 3</c> created a profile named "Compte".
/// </summary>
public static class AndroidShell
{
    /// <summary>
    /// The text as the device's shell will render it whole.
    ///
    /// Single quotes, which suspend all interpretation. An apostrophe
    /// inside closes the quoting: it is closed, an escaped one is
    /// slipped in, then reopened, which is the usual and only safe
    /// way.
    /// </summary>
    public static string Quote(string? value)
    {
        var text = value ?? string.Empty;

        return "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
