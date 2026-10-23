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

    /// <summary>Refusals without a word in a row after which the site is taken as throttling and the run stops.</summary>
    public const int SilentRefusalLimit = 3;

    /// <summary>
    /// The quests "mark all done" sends: not done yet and done once only,
    /// in the order given, each once. Ignored and blocked ones stay out: a
    /// batch never overrides a choice the player made one quest at a time.
    /// </summary>
    public static IReadOnlyList<int> ToMarkDone(IEnumerable<QuestSummary> quests, TrackerProgress? progress)
    {
        ArgumentNullException.ThrowIfNull(quests);

        return
        [
            .. quests
                .Where(q => !QuestRecurrenceLabel.IsRepeatable(q)
                    && QuestTracking.StatusOf(progress, q.Id) is not (QuestStatus.Completed or QuestStatus.Ignored or QuestStatus.Blocked))
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
        var silent = 0;

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
                    // Not cancelled midway: a write the site applied would be reported as not done. The cancel acts in the pause.
                    var write = await tracker.SetStatusAsync(cookie, characterId, id, status, CancellationToken.None).ConfigureAwait(false);

                    if (write.Success)
                    {
                        done.Add(id);
                        silent = 0;
                    }
                    else if (write.After is TrackerReadOutcome.Unreadable or TrackerReadOutcome.NotLinked)
                    {
                        return End(write.After);
                    }
                    else if (!string.IsNullOrWhiteSpace(write.Message))
                    {
                        refused[id] = write.Message;
                        silent = 0;
                    }
                    else if (++silent >= SilentRefusalLimit)
                    {
                        // A site that throttles writes while it still serves reads would loop here.
                        return End(TrackerReadOutcome.Unreadable);
                    }

                    // A lone refusal without a word is left to the read below, which writes it again.
                    progress?.Report(new QuestBatchProgress(done.Count + refused.Count, ids.Count, round));
                }

                await Pace().ConfigureAwait(false);
                var read = await tracker.ReadAsync(cookie, CancellationToken.None).ConfigureAwait(false);
                if (read.Outcome != TrackerReadOutcome.Linked)
                {
                    return End(read.Outcome);
                }

                // The page drops archived characters: rewriting everything for one would be pointless.
                if (read.State?.Progress.TryGetValue(characterId, out var held) != true)
                {
                    return End(TrackerReadOutcome.NotLinked);
                }

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
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // The run must not throw at the window: whatever the tracker did, report it as unreadable with the count kept.
            return End(TrackerReadOutcome.Unreadable);
        }
    }

    private static Task DefaultPause(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1800, 3001)), cancellationToken);
}
