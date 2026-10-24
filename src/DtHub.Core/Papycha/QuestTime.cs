using System.Text.RegularExpressions;
using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>How long a quest takes, estimated from the game's own objectives and maps. Pure.</summary>
public static partial class QuestTime
{
    // Recalibrated 2026-10-23 on 37 random quests against their public durations (a check only, nothing copied):
    // median ratio 0.97. All in seconds.
    private const int Base = 180;
    private const int Talk = 30;
    private const int Map = 30;
    private const int ItemsBase = 10;
    // Harvested items take far longer than 5 s each: "Agriculture et Alchimie" came out at 5 min a quest.
    private const int ItemsEach = 20;
    private const int ItemsCap = 1200;
    private const int Craft = 60;
    private const int PerMonster = 60;
    private const int MonstersCap = 10;
    private const int DungeonRun = 900;
    private const int PerSoul = 300;
    // Most free-text objectives are a one-minute action ("Trouver", "Adressez-vous").
    private const int Vague = 60;
    private const int BossOrDungeon = 900;
    private const int QuestGroup = 1800;
    private const int PerMapTravelled = 4;
    // Past this, a zaap is quicker than walking.
    private const int HopCap = 180;

    // shortcut: French only, the cache held no English or Spanish objective text to check (2026-10-23); add them once seen.
    private static readonly string[] BossOrDungeonWords = ["Braver", "Capturer"];
    private static readonly string[] QuestGroupWords = ["Aider", "Terminer"];
    private static readonly string[] ExitWords = ["Sortie", "Exit", "Salida"];

    /// <summary>The whole quest in minutes, rounded up: a base, each objective, and the way between their maps.</summary>
    public static int EstimateMinutes(TouchQuestFacts facts, (int X, int Y)? start)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var seconds = Base + facts.Objectives.Sum(o => SecondsOf(o, facts)) + TravelSeconds(facts, start);
        return Math.Max(1, (seconds + 59) / 60);
    }

    /// <summary>One objective's own work; the facts tell whether its step is a dungeon run.</summary>
    public static int SecondsOf(TouchObjective objective, TouchQuestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(facts);

        var p = objective.Parameters;
        return objective.TypeId switch
        {
            1 or 9 or 2 => Talk,
            // The dungeon's exit: the run is counted on its fight.
            4 => IsExit(objective) && facts.Objectives.Any(o => SameStep(o, objective) && (IsSingleFight(o) || IsInDungeon(o))) ? 0 : Map,
            3 => Math.Min(ItemsCap, ItemsBase + (ItemsEach * (p.Length > 2 ? p[2] : 0))),
            20 => Craft,
            6 when IsRun(objective, facts) => DungeonRun,
            45 => DungeonRun,
            46 => DungeonRun * Math.Max(1, p.Length > 1 ? p[1] : 1),
            6 or 14 => PerMonster * Math.Clamp(p.Length > 1 ? p[1] : 1, 1, MonstersCap),
            12 => PerSoul,
            // Types 0 and 10, and any type the game adds later.
            _ => StartsWithAny(objective.Text, BossOrDungeonWords) ? BossOrDungeon
                : StartsWithAny(objective.Text, QuestGroupWords) ? QuestGroup
                : Vague,
        };
    }

    /// <summary>True when an objective is a dungeon run: a boss in its dungeon, or a lone fight beside an exit map of its step.</summary>
    public static bool HasDungeonRun(TouchQuestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return facts.Objectives.Any(o => IsRun(o, facts));
    }

    internal static bool IsRun(TouchObjective o, TouchQuestFacts facts) =>
        IsInDungeon(o) || (IsSingleFight(o) && facts.Objectives.Any(e => SameStep(e, o) && IsExit(e)));

    // Types 45 "Vaincre #1 dans son donjon." and 46, the same some times over: the game says it is the dungeon.
    internal static bool IsInDungeon(TouchObjective o) => o.TypeId is 45 or 46;

    // "Sortie du Donjon ...": any other map beside a lone fight ("Chantier du Barrage") made a dungeon run of it.
    private static bool IsExit(TouchObjective o) => o.TypeId == 4 && StartsWithAny(o.Text, ExitWords);

    private static bool StartsWithAny(string? text, string[] words) =>
        text is not null && words.Any(w => text.TrimStart().StartsWith(w, StringComparison.OrdinalIgnoreCase));

    // An unknown step (a cache read before steps were kept) matches none: every fight would pass for a dungeon run.
    private static bool SameStep(TouchObjective a, TouchObjective b) => a.Step is { } step && b.Step == step;

    // "Win 1 in one fight": a boss in practice.
    private static bool IsSingleFight(TouchObjective o) => o.TypeId == 6 && o.Parameters.Length > 1 && o.Parameters[1] == 1;

    /// <summary>Maps walked from the start through each objective with known coordinates, each hop capped at a zaap.</summary>
    public static int TravelSeconds(TouchQuestFacts facts, (int X, int Y)? start)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var seconds = 0;
        var at = start;
        foreach (var o in facts.Objectives)
        {
            if (o.X is not { } x || o.Y is not { } y) continue;
            if (at is { } from) seconds += Math.Min(HopCap, PerMapTravelled * (Math.Abs(x - from.X) + Math.Abs(y - from.Y)));
            at = (x, y);
        }

        return seconds;
    }

    /// <summary>
    /// A dungeon run in minutes: 3 a room and 2 more, from the biggest of its game dungeons; 15 when no room count is
    /// known (a names cache read before rooms were).
    /// </summary>
    /// <param name="rooms">Game dungeon id to its room count.</param>
    public static int DungeonMinutes(IEnumerable<int> gameDungeons, IReadOnlyDictionary<int, int> rooms)
    {
        ArgumentNullException.ThrowIfNull(gameDungeons);
        ArgumentNullException.ThrowIfNull(rooms);

        var most = gameDungeons.Select(id => rooms.GetValueOrDefault(id)).DefaultIfEmpty(0).Max();
        return most > 0 ? (3 * most) + 2 : 15;
    }

    /// <summary>The first "[x,y]" of the quest's start position; null when it has none.</summary>
    public static (int X, int Y)? StartOf(QuestSummary quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        var match = Position().Match(quest.StartPosition);
        return match.Success
            ? (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    [GeneratedRegex(@"\[\s*(-?\d+)\s*,\s*(-?\d+)\s*\]")]
    private static partial Regex Position();

    /// <summary>"≈ 25 min", or "≈ 1 h 05" from an hour.</summary>
    public static string Text(int minutes) =>
        minutes < 60 ? Strings.Format("QuestTimeMinutes", minutes) : Strings.Format("QuestTimeHours", minutes / 60, minutes % 60);
}
