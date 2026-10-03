using System.Globalization;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// The phone's cumulative processor counters at one instant: worth nothing
/// alone, everything once compared with the previous one, see
/// <see cref="PhoneStrain.Between" />.
///
/// **Why it exists.** On 2026-10-03 the game lagged by fits and the panel
/// said nothing. The phone, four days without a restart, had 35 % of its
/// memory available, enough for Android's verdict to stay silent, and spent
/// 38 % of a core in kswapd0 handing it out. Its processor was busy, the
/// game's share of it was not the largest, and none of this was read.
///
/// **Files only.** One shell call reading /proc, nothing like <c>dumpsys
/// input</c>, which held back the delivery of touches for 30 to 60 ms and
/// was itself felt as lag in game.
/// </summary>
/// <param name="Uptime">Time since the phone started.</param>
/// <param name="TotalTicks">Every core's ticks, idle included.</param>
/// <param name="IdleTicks">Idle and I/O wait ticks.</param>
/// <param name="Game">The game's processes and their WebView renderers, ticks by process number.</param>
/// <param name="ReclaimTicks">kswapd0's ticks: the kernel freeing memory.</param>
public sealed record PhoneLoad(
    TimeSpan Uptime,
    long TotalTicks,
    long IdleTicks,
    IReadOnlyDictionary<int, long> Game,
    long ReclaimTicks)
{
    /// <summary>
    /// The shell command, handed to adb as one argument for the phone's
    /// shell to run.
    ///
    /// The renderers are matched by "webview:sandboxed", which Chrome's own
    /// do not carry, written "[w]ebview" so the pattern does not match the
    /// shell running it. Another app's WebView renderer counts as the game:
    /// seven sat on the phone, idle, and only what moves between two
    /// readings counts. Telling them apart would take a <c>dumpsys</c>.
    ///
    /// kswapd0 is found by <c>pgrep -x</c>: <c>pidof</c> reads the command
    /// line, which a kernel thread does not have, and found nothing.
    /// </summary>
    public const string Command =
        "cat /proc/uptime; head -n 1 /proc/stat; echo game; "
        + "for p in $(pidof " + DofusPackages.DofusTouch + ") $(pgrep -f '[w]ebview:sandboxed'); do cat /proc/$p/stat; done; "
        + "echo reclaim; cat /proc/$(pgrep -x kswapd0)/stat";

    /// <summary>
    /// Reads the answer of <see cref="Command" />, or returns <c>null</c>
    /// without the uptime and the processor line.
    /// </summary>
    public static PhoneLoad? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // The first line, and only it: a process line also starts with a number.
        TimeSpan? uptime = double.TryParse(
                lines[0].Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

        long[]? cpu = null;
        Dictionary<int, long> game = [];
        long reclaim = 0;
        var section = string.Empty;

        foreach (var line in lines.Skip(1))
        {
            if (line is "game" or "reclaim")
            {
                section = line;
            }
            else if (cpu is null && line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                cpu = [.. line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(Number)];
            }
            else if (ProcessTicks(line) is { } process)
            {
                if (section == "game")
                {
                    game[process.Id] = process.Ticks;
                }
                else if (section == "reclaim")
                {
                    reclaim = process.Ticks;
                }
            }
        }

        // user nice system idle iowait irq softirq steal.
        return uptime is { } up && cpu is { Length: 8 }
            ? new PhoneLoad(up, cpu.Sum(), cpu[3] + cpu[4], game, reclaim)
            : null;
    }

    /// <summary>
    /// A /proc/[pid]/stat line: the number, then the name in parentheses,
    /// which may hold spaces and parentheses itself, then the counters, of
    /// which user and system time are the 12th and 13th after the name.
    /// </summary>
    private static (int Id, long Ticks)? ProcessTicks(string line)
    {
        var close = line.LastIndexOf(')');
        var open = line.IndexOf(" (", StringComparison.Ordinal);

        if (close < 0 || open <= 0
            || !int.TryParse(line.AsSpan(0, open), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var fields = line[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return fields.Length > 12 ? (id, Number(fields[11]) + Number(fields[12])) : null;
    }

    private static long Number(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

/// <summary>
/// What the phone's processor did between two readings.
/// </summary>
/// <param name="BusyShare">Share of all cores at work.</param>
/// <param name="GameShare">Share of all cores taken by the game and its renderers.</param>
/// <param name="ReclaimCore">kswapd0's time, in cores: 0.38 is 38 % of one.</param>
/// <param name="Uptime">Time since the phone started.</param>
public sealed record PhoneStrain(double BusyShare, double GameShare, double ReclaimCore, TimeSpan Uptime)
{
    /// <summary>
    /// kswapd0's time from which the memory is said to be short. Measured at
    /// 0.38 on the lagging phone of 2026-10-03.
    /// </summary>
    public const double ReclaimLimit = 0.10;

    /// <summary>Share of all cores at work from which the processor is said to be full.</summary>
    public const double BusyLimit = 0.80;

    /// <summary>Days without a restart from which the message says so.</summary>
    public const int LongUptimeDays = 2;

    /// <summary>The longest span two readings may be compared over.</summary>
    private static readonly TimeSpan MaxSpan = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Two minutes in a row, as one: a limit is only crossed if it was
    /// crossed both times. A map load or an app switch can fill one minute;
    /// a phone that stays saturated fills both. And the first calm minute
    /// clears it.
    /// </summary>
    public static PhoneStrain Sustained(PhoneStrain earlier, PhoneStrain later)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(later);

        return later with
        {
            BusyShare = Math.Min(earlier.BusyShare, later.BusyShare),
            ReclaimCore = Math.Min(earlier.ReclaimCore, later.ReclaimCore),
        };
    }

    /// <summary>
    /// Android's clock tick, 100 a second on every ABI it ships: kswapd0's
    /// ticks turn into cores with it.
    /// </summary>
    private const double TicksPerSecond = 100;

    /// <summary>
    /// The difference between two readings, or <c>null</c> when the phone
    /// restarted in between: its counters started again from zero.
    /// </summary>
    public static PhoneStrain? Between(PhoneLoad earlier, PhoneLoad later)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(later);

        var seconds = (later.Uptime - earlier.Uptime).TotalSeconds;
        var total = later.TotalTicks - earlier.TotalTicks;

        // Too old a reading would pass its average off as the last minute.
        if (seconds <= 0 || seconds > MaxSpan.TotalSeconds || total <= 0)
        {
            return null;
        }

        // A number absent the first time is a process started since, whose
        // counters started from zero within the interval: all of its ticks
        // belong to it.
        var game = later.Game.Sum(p =>
            earlier.Game.TryGetValue(p.Key, out var before) ? Math.Max(0, p.Value - before) : p.Value);

        return new PhoneStrain(
            BusyShare: (double)(total - (later.IdleTicks - earlier.IdleTicks)) / total,
            GameShare: (double)game / total,
            ReclaimCore: Math.Max(0, later.ReclaimTicks - earlier.ReclaimTicks) / (seconds * TicksPerSecond),
            Uptime: later.Uptime);
    }

    /// <summary>
    /// The sentence, or <c>null</c> when the phone keeps up. Memory first:
    /// a phone short of it slows everything, the game included, and the
    /// fix is a restart whatever else is busy.
    /// </summary>
    public string? Describe()
    {
        var days = (int)Uptime.TotalDays;
        var uptime = days >= LongUptimeDays ? Strings.Format("PhoneUptime", days) : string.Empty;

        if (ReclaimCore >= ReclaimLimit)
        {
            return Strings.Format("PhoneMemoryStrain", uptime);
        }

        if (BusyShare < BusyLimit)
        {
            return null;
        }

        // The game holds at least half of what is busy: lowering its
        // graphics or the stream is what frees the processor.
        return GameShare >= BusyShare / 2
            ? Strings.Format("PhoneGameHeavy", Math.Round(GameShare * 100))
            : Strings.Format("PhoneBusy", uptime);
    }
}
