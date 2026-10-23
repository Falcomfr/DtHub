using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>A quest batch as the guides window says it: the progress line and the closing summary.</summary>
public static class QuestBatchText
{
    /// <summary>"42 / 140", then the pass once the run writes again what did not hold: the count can go down.</summary>
    public static string Progress(QuestBatchProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return progress.Round > 1
            ? Strings.Format("BatchProgressRound", progress.Handled, progress.Total, progress.Round)
            : Strings.Format("BatchProgress", progress.Handled, progress.Total);
    }

    /// <summary>
    /// What was done, what did not hold, and each refusal in the site's words. A run cut short could not check its
    /// last writes: it speaks of writes the site acknowledged, and says the tracker is read again.
    /// </summary>
    public static string Summary(QuestBatchResult result, Func<int, string> titleOf)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(titleOf);

        var early = result.Cancelled || result.StoppedBy is not null;
        var lines = new List<string>();

        if (early)
        {
            lines.Add(Strings.Get(result.Cancelled ? "BatchCancelled"
                : result.StoppedBy == TrackerReadOutcome.NotLinked ? "BatchLinkLost" : "BatchSiteDown"));
            lines.Add(Strings.Format("BatchAcknowledged", result.Done.Count, result.Missing.Count));
        }
        else
        {
            var done = Strings.Format("BatchDone", result.Done.Count);
            lines.Add(result.Missing.Count > 0 ? $"{done} · {Strings.Format("BatchMissing", result.Missing.Count)}" : done);
        }

        if (result.Refused.Count > 0)
        {
            lines.Add(Strings.Format("BatchRefused", result.Refused.Count));
            lines.AddRange(result.Refused.Select(r => Strings.Format("BatchRefusalLine", titleOf(r.QuestId), r.Message)));
        }

        if (early)
        {
            lines.Add(Strings.Get("BatchRereading"));
        }

        return string.Join("\n", lines);
    }
}
