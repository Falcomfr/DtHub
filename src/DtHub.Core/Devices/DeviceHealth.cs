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
    /// <summary>The PC's processor load, in percent, from which it is said to lag.</summary>
    public const int PcBusyLimit = 85;

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
    /// <param name="strain">
    /// What the phone's processor did over the last minute.
    /// </param>
    /// <param name="pcBusyPercent">
    /// The PC's processor load over the last minute, given to one device
    /// only: the PC is shared, and saying it under every phone would be
    /// lines too many.
    /// </param>
    /// <param name="lockedWindows">
    /// True when this device's game windows show its lock screen
    /// instead of the game, that is, when its virtual display follows
    /// the lock state and it is locked.
    /// </param>
    public static IReadOnlyList<HealthFinding> Review(
        ThermalReading? heat,
        BatteryReading? battery,
        StorageReading? storage,
        bool lockedWindows = false,
        bool unpreparedBattery = false,
        bool deadInput = false,
        MemoryReading? memory = null,
        PhoneStrain? strain = null,
        int? pcBusyPercent = null)
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

        // Beside the free space and for the same reason: nothing is broken
        // yet, but everything slows. It is the answer to "it lags after an
        // hour", which was put down to DT Hub the first time.
        if (memory?.Describe() is { } tight)
        {
            findings.Add(new HealthFinding(
                memory.Level >= MemoryPressure.Critical ? HealthSeverity.Serious : HealthSeverity.Warning,
                tight));
        }

        // **One lag finding at most**, asked for so the panel does not
        // fill up: the phone first, the cause that is easiest to fix, then
        // the PC. Memory strain already told by Android's verdict is not
        // told twice. The Wi-Fi has no line at all: see the end.
        var lag = strain?.Describe() is { } strained
                && !(memory?.IsLow == true && strain.ReclaimCore >= PhoneStrain.ReclaimLimit)
            ? strained
            : pcBusyPercent >= PcBusyLimit
                ? Strings.Format("PcBusy", pcBusyPercent)
                : null;

        if (lag is not null)
        {
            findings.Add(new HealthFinding(HealthSeverity.Warning, lag));
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

        // **The Wi-Fi never takes a line.** A 2.4 GHz band and a crowded
        // channel used to, under every phone, all session long: the player
        // had already changed channel, nothing changed, and the line stayed,
        // which taught to ignore the panel. The link's chip beside the name
        // says it in a word, and its tooltip gives the advice.
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
