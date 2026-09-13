namespace DtHub.Core.Devices;

/// <summary>
/// What the phone does with an input event we send it.
/// </summary>
public enum InputInjection
{
    /// <summary>We could not tell, and so we do not conclude.</summary>
    Unknown = 0,

    /// <summary>The device accepts simulated input.</summary>
    Works,

    /// <summary>
    /// The device refuses it: the picture will get through, clicks
    /// will not.
    /// </summary>
    Denied,
}

/// <summary>
/// Tests the one thing no message ever announces: the right to
/// inject input.
///
/// This is the most common symptom in the field, and the most
/// helpless one. scrcpy does not fail, no error appears, the window
/// opens and the click does nothing. The cause lies in a setting on
/// Xiaomi, Oppo, realme and vivo overlays, "Grant permissions and
/// simulate input via USB debugging". Without it, ADB displays but
/// does not inject.
///
/// The probe sends Android's "unknown" key, which triggers nothing
/// anywhere: it is not a game command, it is a question asked of
/// the system.
///
/// **It used to be sent only on request, from the help page. The
/// field made us change the rule.** Twice on the same day, windows
/// showed the game without responding to anything, and the faulty
/// setting unchecks itself on restart on devices without a SIM
/// card: nobody has a reason to go open a help page in front of a
/// window that looks normal. It is therefore asked once per device,
/// the moment its first window opens, and never during a game
/// session nor repeatedly.
///
/// The verdict is cautious by design. We only say "it works" on
/// complete silence, and "refused" only on a named refusal.
/// Everything else is <see cref="InputInjection.Unknown"/>: getting
/// the diagnosis wrong would cost more than giving none at all.
/// </summary>
public static class InputInjectionCheck
{
    /// <summary>
    /// The "unknown" key. Android accepts it everywhere and does
    /// nothing with it: that is what lets us ask the question
    /// without acting on the device.
    /// </summary>
    public const string ProbeKeyCode = "0";

    /// <summary>Command sent to the device's shell.</summary>
    public static IReadOnlyList<string> ProbeCommand { get; } =
        ["shell", "input", "keyevent", ProbeKeyCode];

    /// <summary>
    /// What Android writes when it refuses. The wording varies from
    /// one version and one overlay to another: so we look for what
    /// does not vary.
    /// </summary>
    private static readonly string[] Refusals =
    [
        "inject_events",
        "injecting input",
        "injecting to another application",
        "not allowed to inject",
    ];

    /// <summary>What states a refusal without naming injection.</summary>
    private static readonly string[] Denials =
    [
        "securityexception",
        "permission denial",
        "permission denied",
    ];

    /// <summary>Reads the verdict from what the probe returned.</summary>
    /// <param name="exitCode">Exit code of the command.</param>
    /// <param name="standardOutput">Standard output.</param>
    /// <param name="standardError">Error output.</param>
    public static InputInjection Read(int exitCode, string? standardOutput, string? standardError)
    {
        var said = ((standardOutput ?? string.Empty) + "\n" + (standardError ?? string.Empty))
            .ToLowerInvariant();

        if (Refusals.Any(r => said.Contains(r, StringComparison.Ordinal)))
        {
            return InputInjection.Denied;
        }

        // A generic refusal only counts if it actually talks about
        // input: the shell returns the same family of error for a
        // locked secure folder, which has nothing to do with the
        // mouse.
        if (Denials.Any(d => said.Contains(d, StringComparison.Ordinal))
            && said.Contains("input", StringComparison.Ordinal))
        {
            return InputInjection.Denied;
        }

        // Success is silent: the unknown key produces no output. A
        // command producing output we cannot interpret proves
        // nothing.
        return exitCode == 0 && string.IsNullOrWhiteSpace(said)
            ? InputInjection.Works
            : InputInjection.Unknown;
    }
}
