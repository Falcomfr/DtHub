namespace DtHub.Core.Devices;

/// <summary>
/// Is the phone's own screen lit, the panel and not the game's display.
///
/// **Why it exists.** The screen-off session switches the panel off once,
/// when the first account opens. On a Galaxy S25 Ultra, Android 16, a
/// player who picked the phone up to answer a call saw it stay lit for
/// good afterwards, whatever its sleep delay. The 13T Pro goes back to
/// sleep on its own: the difference was not reproduced, only remedied, by
/// switching the panel off again on request.
///
/// Read from <c>dumpsys SurfaceFlinger --displays</c>, about 1.5 KB where
/// the whole dump is 73 KB. Measured on the 13T Pro in game, read every
/// half second for twenty seconds: 21.0 % of late frames against 21.4 %
/// without.
///
/// Android's own wakefulness says nothing here: the panel is switched off
/// behind its back and the phone stays "Awake".
/// </summary>
public static class PanelPower
{
    /// <summary>The shell command, one argument per word.</summary>
    public static readonly IReadOnlyList<string> Command = ["dumpsys", "SurfaceFlinger", "--displays"];

    /// <summary>
    /// True if one physical display is on, <c>null</c> when the answer
    /// names none.
    ///
    /// The game's virtual display is listed too, under "Virtual Display",
    /// and is always on: only the sections opened by "Display" count.
    /// "Doze" is the screen a sleeping phone keeps for the clock, not a
    /// screen somebody lit.
    /// </summary>
    public static bool? IsLit(string? dump)
    {
        if (string.IsNullOrWhiteSpace(dump))
        {
            return null;
        }

        bool? lit = null;
        var physical = false;

        foreach (var raw in dump.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();

            if (!char.IsWhiteSpace(raw[0]))
            {
                physical = line.StartsWith("Display ", StringComparison.Ordinal);
                continue;
            }

            // The first word only: some versions follow the mode with its
            // number, "powerMode=On (2)". Samsung writes it in capitals,
            // "ON", and its lit screen read dark (2026-10-21).
            if (physical && line.StartsWith("powerMode=", StringComparison.Ordinal))
            {
                lit = lit == true || string.Equals(line["powerMode=".Length..].Split(' ')[0], "On", StringComparison.OrdinalIgnoreCase);
            }
        }

        return lit;
    }
}

/// <summary>
/// What a phone answered about its screen: the verdict, and the raw
/// answer, or the error when the question failed.
/// </summary>
public sealed record PanelReading(bool? Lit, string Answer, bool Failed);
