using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// Severity of a finding, from the most trivial to the most urgent.
/// </summary>
public enum HealthSeverity
{
    /// <summary>Worth knowing, does not prevent anything.</summary>
    Notice,

    /// <summary>Will get in the way, and can be fixed.</summary>
    Warning,

    /// <summary>Will end the session if nothing is done.</summary>
    Serious,
}

/// <summary>A finding about a device's state.</summary>
public readonly record struct HealthFinding(HealthSeverity Severity, string Message);

/// <summary>
/// The health check of a device, before launching anything.
///
/// The application already read temperature and the link, and now the
/// battery and free space. **Each spoke on its own**, and none spoke
/// before launch: the problem was discovered once the windows were
/// open, that is, too late to avoid it.
///
/// The health check lines them up, from the most serious to the most
/// trivial. It decides nothing and prevents nothing: it says what is
/// going to get in the way, to whoever wants to read it.
///
/// **The messages come from the readings themselves**, not from here.
/// Rewriting what <see cref="ThermalReading.Describe" /> already says
/// would give two texts for the same fact, which would eventually
/// drift apart.
/// </summary>
public static class DeviceHealth
{
    /// <summary>
    /// The findings, from the most serious to the most trivial, at
    /// equal severity in the order they matter: what ends the
    /// session, then what gets in its way.
    /// </summary>
    /// <param name="unpreparedBattery">
    /// True when the game is not shielded from battery saving on this
    /// device, that is, when the preparation described in the help
    /// was never done on it.
    /// </param>
    /// <param name="deadInput">
    /// True when the device refuses input coming from the PC: the
    /// windows show the game and respond to nothing.
    /// </param>
    /// <param name="lockedWindows">
    /// True when this device's game windows show its lock screen
    /// instead of the game, that is, when its virtual display follows
    /// the lock state and it is locked.
    /// </param>
    /// <summary>
    /// Share of retransmitted frames beyond which the channel is
    /// worth naming. It is <see cref="VideoBuffer" />'s own worst
    /// bucket: below it the buffer absorbs the stutter, above it the
    /// buffer has already given everything it has.
    /// </summary>
    private const double CrowdedChannel = 0.20;

    public static IReadOnlyList<HealthFinding> Review(
        ThermalReading? heat,
        BatteryReading? battery,
        StorageReading? storage,
        WifiLink? link,
        bool lockedWindows = false,
        bool unpreparedBattery = false,
        bool deadInput = false)
    {
        List<HealthFinding> findings = [];

        // First because nothing else matters while it lasts: the
        // windows are open and do not show the game. And here rather
        // than at launch, where the message only lived for two
        // seconds before the next sweep replaced it.
        if (lockedWindows)
        {
            findings.Add(new HealthFinding(HealthSeverity.Serious, Strings.Get("DisplayStaysLocked")));
        }

        // Right behind the lock, and for the same reason: the window
        // may well show the game, it is still useless. It is the most
        // silent symptom found in the field, the one that takes an
        // evening to name.
        if (deadInput)
        {
            findings.Add(new HealthFinding(HealthSeverity.Serious, Strings.Get("DeadInputFound")));
        }

        if (battery?.Describe() is { } power && battery.Concern is { } level)
        {
            findings.Add(new HealthFinding(level, power));
        }

        if (storage?.Describe() is { } room)
        {
            findings.Add(new HealthFinding(
                storage.FreeBytes <= StorageReading.Critical ? HealthSeverity.Serious : HealthSeverity.Warning,
                room));
        }

        if (heat?.Describe() is { } warm)
        {
            findings.Add(new HealthFinding(
                heat.Status >= ThermalReading.Severe ? HealthSeverity.Serious : HealthSeverity.Warning,
                warm));
        }

        // After everything that already gets in the way, before what
        // does not get in the way yet: the game runs, and nothing
        // shows as long as Android does not step in. But it is the
        // top cause of windows that freeze, and the application knew
        // it without ever checking.
        if (unpreparedBattery)
        {
            findings.Add(new HealthFinding(HealthSeverity.Warning, Strings.Get("BatteryNotPrepared")));
        }

        // The link breaks nothing and is already compensated for on
        // its own by the video buffer. It is stated so that "it
        // stutters" has an answer, not to alarm: hence the lowest
        // rank.
        if (link is { Is24GHz: true })
        {
            findings.Add(new HealthFinding(HealthSeverity.Notice, Strings.Get("DeviceOn24GHz")));
        }

        // **A congested 5 GHz channel said nothing at all.** The band
        // was the only thing the link could report, so a clean 2.4 GHz
        // was named while a 5 GHz losing four frames in ten was not.
        // The retry share was measured, written to the log and fed to
        // the video buffer, and never reached the screen: "why is the
        // window black for so long" had no answer anywhere.
        //
        // The threshold is the video buffer's own worst bucket. Below
        // it, the buffer absorbs the stutter and there is nothing to
        // say; above it, it has already given everything it has.
        else if (link is { RetryShare: >= CrowdedChannel })
        {
            findings.Add(new HealthFinding(
                HealthSeverity.Notice,
                Strings.Format("DeviceLinkCrowded", Math.Round(link.RetryShare * 100))));
        }

        return [.. findings.OrderByDescending(f => f.Severity)];
    }

    /// <summary>
    /// All the findings, one per line, or <c>null</c> when there are
    /// none.
    ///
    /// **The banner shows the worst, the tooltip shows everything**,
    /// and that distinction was paid for dearly: a phone carried three
    /// findings at once, lock, low storage and unprepared battery, of
    /// which only one showed. The other two were nowhere, not even on
    /// hover: the first had to be fixed to discover the second. A
    /// line stays a line, but what it hides must stay reachable.
    /// </summary>
    public static string? Every(IReadOnlyList<HealthFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return findings.Count == 0
            ? null
            : string.Join(Environment.NewLine, findings.Select(f => f.Message));
    }
}
