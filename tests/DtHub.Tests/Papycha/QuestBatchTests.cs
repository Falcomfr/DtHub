using DtHub.Core.Papycha;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Papycha;

public class QuestBatchTests
{
    private const string Cookie = "phrase";
    private const string Hero = "b";

    /// <summary>
    /// A tracker that keeps one character's statuses, loses the first
    /// write of the quests told to, refuses others with a message, and
    /// can fail or run code on a given write.
    /// </summary>
    private sealed class FakeTracker : IPapychaTracker
    {
        public Dictionary<int, QuestStatus> Statuses { get; } = [];

        public Dictionary<int, int> Drops { get; } = [];

        public Dictionary<int, string> Refusals { get; } = [];

        // Refusals without a word, as a throttling site gives, counted down per quest.
        public Dictionary<int, int> Silent { get; } = [];

        // Quests the site infers as done without storing a status.
        public HashSet<int> Inferred { get; } = [];

        public bool ReadThrows { get; init; }

        public bool Archived { get; init; }

        public List<int> Writes { get; } = [];

        public int Reads { get; private set; }

        public int? FailOnWrite { get; init; }

        public Action<int>? OnWrite { get; init; }

        public Task<TrackerReadResult> ReadAsync(string cookie, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (ReadThrows)
            {
                throw new InvalidOperationException("unexpected page");
            }

            var progress = new TrackerProgress(
                new Dictionary<int, QuestStatus>(Statuses),
                Statuses.Where(s => s.Value == QuestStatus.Completed).Select(s => s.Key).Concat(Inferred).ToHashSet(),
                new HashSet<int>(),
                []);
            var state = new TrackerState("n", [new TrackerCharacter(Hero, "Falcom", 190)], Hero, Archived ? [] : new Dictionary<string, TrackerProgress> { [Hero] = progress });

            return Task.FromResult(new TrackerReadResult(TrackerReadOutcome.Linked, state));
        }

        public Task<TrackerWriteResult> SetStatusAsync(
            string cookie, string characterId, int questId, QuestStatus status, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(Cookie, cookie);
            Assert.Equal(Hero, characterId);
            Writes.Add(questId);
            OnWrite?.Invoke(Writes.Count);

            if (FailOnWrite == Writes.Count)
            {
                return Task.FromResult(new TrackerWriteResult(false, null, TrackerReadOutcome.Unreadable));
            }

            if (Refusals.TryGetValue(questId, out var message))
            {
                return Task.FromResult(new TrackerWriteResult(false, message));
            }

            if (Silent.TryGetValue(questId, out var silent) && silent > 0)
            {
                Silent[questId] = silent - 1;
                return Task.FromResult(new TrackerWriteResult(false, null, TrackerReadOutcome.Linked));
            }

            // The site answers success but forgets the write.
            if (Drops.TryGetValue(questId, out var left) && left > 0)
            {
                Drops[questId] = left - 1;
            }
            else
            {
                Statuses[questId] = status;
            }

            return Task.FromResult(new TrackerWriteResult(true, null));
        }

        public Task<TrackerWriteResult> SaveCharacterAsync(
            string cookie, TrackerCharacter character, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static Task NoPause(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Task<QuestBatchResult> Run(
        FakeTracker tracker, int[] ids, IProgress<QuestBatchProgress>? progress = null, Func<CancellationToken, Task>? pause = null, CancellationToken cancellationToken = default) =>
        QuestBatch.RunAsync(tracker, Cookie, Hero, ids, QuestStatus.Completed, progress, pause ?? NoPause, cancellationToken);

    [Fact]
    public async Task Les_ecritures_perdues_par_le_site_sont_reecrites_jusqu_a_tenir()
    {
        var tracker = new FakeTracker { Drops = { [2] = 1, [4] = 2 } };

        var result = await Run(tracker, [1, 2, 3, 4]);

        Assert.Equal([1, 2, 3, 4], result.Done);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Refused);
        Assert.Null(result.StoppedBy);
        Assert.False(result.Cancelled);
        Assert.Equal([1, 2, 3, 4, 2, 4, 4], tracker.Writes);
        Assert.Equal(3, tracker.Reads);
    }

    [Fact]
    public async Task Une_quete_que_le_site_perd_toujours_finit_manquante_apres_trois_tours_de_plus()
    {
        var tracker = new FakeTracker { Drops = { [2] = 99 } };

        var result = await Run(tracker, [1, 2]);

        Assert.Equal([1], result.Done);
        Assert.Equal([2], result.Missing);
        Assert.Equal([1, 2, 2, 2, 2], tracker.Writes);
        Assert.Equal(4, tracker.Reads);
    }

    [Fact]
    public async Task Un_refus_avec_message_est_definitif_et_n_est_pas_reecrit()
    {
        var tracker = new FakeTracker { Drops = { [1] = 1 }, Refusals = { [2] = "Statut invalide." } };

        var result = await Run(tracker, [1, 2, 3]);

        Assert.Equal([1, 3], result.Done);
        var refusal = Assert.Single(result.Refused);
        Assert.Equal(new QuestBatchRefusal(2, "Statut invalide."), refusal);
        Assert.Empty(result.Missing);
        Assert.Equal([1, 2, 3, 1], tracker.Writes);
    }

    [Fact]
    public async Task Une_annulation_en_cours_arrete_proprement_et_dit_ce_qui_est_fait()
    {
        using var cancel = new CancellationTokenSource();
        var tracker = new FakeTracker { OnWrite = n => { if (n == 2) { cancel.Cancel(); } } };

        var result = await Run(tracker, [1, 2, 3, 4], pause: ct => Task.Delay(0, ct), cancellationToken: cancel.Token);

        Assert.True(result.Cancelled);
        Assert.Equal([1, 2], result.Done);
        Assert.Equal([3, 4], result.Missing);
        Assert.Equal([1, 2], tracker.Writes);
    }

    [Fact]
    public async Task Une_panne_reseau_arrete_le_lot_et_laisse_le_reste_manquant_avec_la_raison()
    {
        var tracker = new FakeTracker { FailOnWrite = 2 };

        var result = await Run(tracker, [1, 2, 3]);

        Assert.Equal(TrackerReadOutcome.Unreadable, result.StoppedBy);
        Assert.Equal([1], result.Done);
        Assert.Equal([2, 3], result.Missing);
        Assert.Equal([1, 2], tracker.Writes);
        Assert.Equal(0, tracker.Reads);
    }

    [Fact]
    public async Task La_progression_compte_chaque_quete_traitee_et_le_tour_en_cours()
    {
        var tracker = new FakeTracker { Drops = { [2] = 1 }, Refusals = { [3] = "Statut invalide." } };
        List<QuestBatchProgress> seen = [];

        await Run(tracker, [1, 2, 3], new SyncProgress<QuestBatchProgress>(seen.Add));

        Assert.Equal(
            [new(1, 3, 1), new(2, 3, 1), new(3, 3, 1), new(3, 3, 2)],
            seen);
    }

    [Fact]
    public async Task Une_pause_separe_chaque_appel_au_site_sauf_le_premier()
    {
        var tracker = new FakeTracker();
        var pauses = 0;

        await Run(tracker, [1, 2, 2, 3], pause: _ => { pauses++; return Task.CompletedTask; });

        // Three writes (the duplicate goes once) and one read.
        Assert.Equal([1, 2, 3], tracker.Writes);
        Assert.Equal(3, pauses);
    }

    [Fact]
    public async Task Un_lot_vide_ne_touche_pas_au_site()
    {
        var tracker = new FakeTracker();

        var result = await Run(tracker, []);

        Assert.Empty(result.Done);
        Assert.Empty(tracker.Writes);
        Assert.Equal(0, tracker.Reads);
    }

    [Fact]
    public async Task Un_refus_sans_message_est_reecrit_et_finit_par_tenir()
    {
        var tracker = new FakeTracker { Silent = { [2] = 1 } };

        var result = await Run(tracker, [1, 2]);

        Assert.Equal([1, 2], result.Done);
        Assert.Empty(result.Missing);
        Assert.Equal([1, 2, 2], tracker.Writes);
    }

    [Fact]
    public async Task Trois_refus_sans_message_de_suite_arretent_le_lot_et_un_succes_remet_le_compte_a_zero()
    {
        var tracker = new FakeTracker { Silent = { [1] = 99, [2] = 99, [4] = 99, [5] = 99 } };

        var result = await Run(tracker, [1, 2, 3, 4, 5]);

        Assert.Equal(TrackerReadOutcome.Unreadable, result.StoppedBy);
        Assert.Equal([3], result.Done);
        Assert.Equal([1, 2, 4, 5], result.Missing);
        Assert.Equal([1, 2, 3, 4, 5, 1], tracker.Writes);
    }

    [Fact]
    public async Task Une_erreur_inattendue_du_suivi_arrete_le_lot_sans_perdre_le_compte()
    {
        var tracker = new FakeTracker { ReadThrows = true };

        var result = await Run(tracker, [1, 2]);

        Assert.Equal(TrackerReadOutcome.Unreadable, result.StoppedBy);
        Assert.Equal([1, 2], result.Done);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public async Task Un_personnage_archive_en_cours_de_route_arrete_le_lot_sans_tout_reecrire()
    {
        var tracker = new FakeTracker { Archived = true };

        var result = await Run(tracker, [1, 2]);

        Assert.Equal(TrackerReadOutcome.NotLinked, result.StoppedBy);
        Assert.Equal([1, 2], tracker.Writes);
        Assert.Equal([1, 2], result.Done);
    }

    [Fact]
    public async Task Une_quete_que_le_site_deduit_terminee_compte_comme_faite()
    {
        var tracker = new FakeTracker { Drops = { [2] = 99 }, Inferred = { 2 } };

        var result = await Run(tracker, [1, 2]);

        Assert.Equal([1, 2], result.Done);
        Assert.Equal([1, 2], tracker.Writes);
    }

    [Fact]
    public void Tout_marquer_termine_garde_les_quetes_non_terminees_non_repetables_dans_l_ordre_sans_doublon()
    {
        QuestSummary[] quests =
        [
            new() { Id = 5 },
            new() { Id = 1 },
            new() { Id = 2, Recurrence = QuestRecurrence.Daily },
            new() { Id = 3, SectionIds = [QuestRecurrenceLabel.RepeatableSection] },
            new() { Id = 4 },
            new() { Id = 6 },
            new() { Id = 7 },
            new() { Id = 8 },
            new() { Id = 5 },
        ];
        var progress = new TrackerProgress(
            new Dictionary<int, QuestStatus> { [4] = QuestStatus.Completed, [6] = QuestStatus.InProgress, [7] = QuestStatus.Ignored, [8] = QuestStatus.Blocked },
            new HashSet<int> { 1, 4 },
            new HashSet<int>(),
            []);

        Assert.Equal([5, 6], QuestBatch.ToMarkDone(quests, progress));
    }
}
