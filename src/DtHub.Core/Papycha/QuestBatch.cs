namespace DtHub.Core.Papycha;

/// <summary>Where a batch stands: quests written or refused so far, out of all, and the pass under way (1 is the first).</summary>
public sealed record QuestBatchProgress(int Handled, int Total, int Round);

/// <summary>A quest the site refused for good, with its words (the "Dévotion à …" quests get "Statut invalide.").</summary>
public sealed record QuestBatchRefusal(int QuestId, string Message);

/// <summary>
/// How a batch ended, each list in the order given. <paramref name="Done"/>
/// holds what the last read confirmed, plus what the site acknowledged since:
/// a run stopped early could not check those last writes.
/// </summary>
/// <param name="StoppedBy">Unreadable (network) or NotLinked (dead cookie) when the site cut the run short.</param>
public sealed record QuestBatchResult(
    IReadOnlyList<int> Done,
    IReadOnlyList<QuestBatchRefusal> Refused,
    IReadOnlyList<int> Missing,
    TrackerReadOutcome? StoppedBy,
    bool Cancelled);

/// <summary>
/// Sets many quests to one status on the tracker. Measured on 2026-10-23:
/// papycha.fr answers success but loses about one write in seven when they
/// follow each other quickly, so the run paces its calls, reads the tracker
/// back after each pass and writes again what did not hold.
/// </summary>
public static class QuestBatch
{
    /// <summary>Passes after the first; the import converged in one or two.</summary>
    public const int ExtraRounds = 3;

    /// <summary>
    /// The quests "mark all done" sends: not done yet and done once only,
    /// in the order given, each once.
    /// </summary>
    public static IReadOnlyList<int> ToMarkDone(IEnumerable<QuestSummary> quests, TrackerProgress? progress)
    {
        ArgumentNullException.ThrowIfNull(quests);

        return
        [
            .. quests
                .Where(q => !QuestRecurrenceLabel.IsRepeatable(q)
                    && QuestTracking.StatusOf(progress, q.Id) != QuestStatus.Completed)
                .Select(q => q.Id)
                .Distinct(),
        ];
    }

    /// <summary>
    /// Writes <paramref name="status"/> for each quest, one call at a time
    /// with <paramref name="pause"/> between calls (1.8 to 3 s by default).
    /// Never throws: a cancellation or a site that stops answering ends the
    /// run with what was done. The cookie is only passed to the tracker.
    /// </summary>
    public static async Task<QuestBatchResult> RunAsync(
        IPapychaTracker tracker,
        string cookie,
        string characterId,
        IEnumerable<int> questIds,
        QuestStatus status,
        IProgress<QuestBatchProgress>? progress = null,
        Func<CancellationToken, Task>? pause = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(questIds);

        pause ??= DefaultPause;
        var ids = questIds.Distinct().ToList();
        var done = new HashSet<int>();
        var refused = new Dictionary<int, string>();
        var first = true;

        async Task Pace()
        {
            if (!first)
            {
                await pause(cancellationToken).ConfigureAwait(false);
            }

            first = false;
            cancellationToken.ThrowIfCancellationRequested();
        }

        QuestBatchResult End(TrackerReadOutcome? stoppedBy = null, bool cancelled = false) => new(
            [.. ids.Where(done.Contains)],
            [.. ids.Where(refused.ContainsKey).Select(id => new QuestBatchRefusal(id, refused[id]))],
            [.. ids.Where(id => !done.Contains(id) && !refused.ContainsKey(id))],
            stoppedBy,
            cancelled);

        if (ids.Count == 0)
        {
            return End();
        }

        try
        {
            var todo = ids;
            for (var round = 1; round <= 1 + ExtraRounds && todo.Count > 0; round++)
            {
                foreach (var id in todo)
                {
                    await Pace().ConfigureAwait(false);
                    var write = await tracker.SetStatusAsync(cookie, characterId, id, status, cancellationToken).ConfigureAwait(false);

                    if (write.Success)
                    {
                        done.Add(id);
                    }
                    else if (write.After is TrackerReadOutcome.Unreadable or TrackerReadOutcome.NotLinked)
                    {
                        return End(write.After);
                    }
                    else if (!string.IsNullOrWhiteSpace(write.Message))
                    {
                        refused[id] = write.Message;
                    }

                    // A refusal without a word is left to the read below, which writes it again.
                    progress?.Report(new QuestBatchProgress(done.Count + refused.Count, ids.Count, round));
                }

                await Pace().ConfigureAwait(false);
                var read = await tracker.ReadAsync(cookie, cancellationToken).ConfigureAwait(false);
                if (read.Outcome != TrackerReadOutcome.Linked)
                {
                    return End(read.Outcome);
                }

                var held = read.State?.Progress.GetValueOrDefault(characterId);
                todo = [.. ids.Where(id => !refused.ContainsKey(id) && QuestTracking.StatusOf(held, id) != status)];
                var lost = todo.ToHashSet();
                done = [.. ids.Where(id => !refused.ContainsKey(id) && !lost.Contains(id))];
            }

            return End();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The player's own cancel: the result says so and lists what is left.
            return End(cancelled: true);
        }
    }

    private static Task DefaultPause(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1800, 3001)), cancellationToken);
}
