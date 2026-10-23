using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestBatchTextTests
{
    private static string Title(int id) => id == 4 ? "Dévotion à Xélor" : "Q" + id;

    [Fact]
    public void La_progression_donne_le_compte_puis_la_passe_au_dela_de_la_premiere()
    {
        Assert.Equal("42 / 140", QuestBatchText.Progress(new QuestBatchProgress(42, 140, 1)));
        Assert.Equal("40 / 140 · passe 2", QuestBatchText.Progress(new QuestBatchProgress(40, 140, 2)));
    }

    [Fact]
    public void Un_lot_fini_dit_ce_qui_est_fait_ce_qui_manque_et_chaque_refus_avec_les_mots_du_site()
    {
        var result = new QuestBatchResult([1, 2, 3], [new QuestBatchRefusal(4, "Statut invalide.")], [5], null, false);

        Assert.Equal(
            "Marquées terminées : 3 · non enregistrées : 1\nRefusées par le site : 1\nDévotion à Xélor : Statut invalide.",
            QuestBatchText.Summary(result, Title));
    }

    [Fact]
    public void Un_lot_fini_sans_manque_ni_refus_tient_en_une_ligne() =>
        Assert.Equal("Marquées terminées : 2", QuestBatchText.Summary(new QuestBatchResult([1, 2], [], [], null, false), Title));

    [Fact]
    public void Un_lot_annule_parle_d_ecritures_acceptees_et_annonce_la_relecture() =>
        Assert.Equal(
            "Arrêté.\nÉcritures acceptées par le site : 1 · non faites : 2\nLe suivi est relu pour montrer l'état réel.",
            QuestBatchText.Summary(new QuestBatchResult([1], [], [2, 3], null, true), Title));

    [Fact]
    public void Un_lot_coupe_par_le_site_dit_pourquoi()
    {
        Assert.StartsWith(
            "Le site ne répond plus.\n",
            QuestBatchText.Summary(new QuestBatchResult([], [], [1], TrackerReadOutcome.Unreadable, false), Title));
        Assert.StartsWith(
            "Le site n'accepte plus la liaison.\n",
            QuestBatchText.Summary(new QuestBatchResult([], [], [1], TrackerReadOutcome.NotLinked, false), Title));
    }
}
