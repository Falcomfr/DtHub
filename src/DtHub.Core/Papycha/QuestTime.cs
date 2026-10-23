using System.Text.RegularExpressions;
using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>How long a quest takes, estimated from the game's own objectives and maps. Pure.</summary>
public static partial class QuestTime
{
    // Claude's first calibration (2026-10-23), checked against a sample of published quest durations, nothing copied:
    // to be tuned from the user's feedback. All in seconds.
    private const int Base = 90;
    private const int Talk = 15;
    private const int Map = 45;
    private const int ItemsBase = 10;
    private const int ItemsEach = 5;
    private const int ItemsCap = 300;
    private const int Craft = 60;
    private const int PerMonster = 90;
    private const int MonstersCap = 10;
    private const int DungeonRun = 900;
    private const int PerSoul = 300;
    private const int Vague = 300;
    private const int PerMapTravelled = 8;
    // Past this, a zaap is quicker than walking.
    private const int HopCap = 300;

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
            4 => facts.Objectives.Any(o => SameStep(o, objective) && IsSingleFight(o)) ? 0 : Map,
            3 => Math.Min(ItemsCap, ItemsBase + (ItemsEach * (p.Length > 2 ? p[2] : 0))),
            20 => Craft,
            6 when IsSingleFight(objective) && facts.Objectives.Any(o => SameStep(o, objective) && o.TypeId == 4) => DungeonRun,
            6 or 14 => PerMonster * Math.Clamp(p.Length > 1 ? p[1] : 1, 1, MonstersCap),
            12 => PerSoul,
            // Types 0 and 10, and any type the game adds later.
            _ => Vague,
        };
    }

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
