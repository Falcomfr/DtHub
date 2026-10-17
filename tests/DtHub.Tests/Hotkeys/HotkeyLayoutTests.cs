using DtHub.Core.Hotkeys;

namespace DtHub.Tests.Hotkeys;

/// <summary>
/// The editor used to list twenty-two shortcuts one under the other, F1 to
/// F8 each on its own line: "ça fait gavé de raccourcis et c'est galère de
/// comprendre" (2026-10-16). They are now in three sections, the series
/// folded into one line.
/// </summary>
public class HotkeyLayoutTests
{
    [Fact]
    public void Chaque_action_est_dans_une_seule_section()
    {
        var placed = HotkeyLayout.Sections
            .SelectMany(s => s.Entries)
            .SelectMany(e => e)
            .ToList();

        // Leaving fullscreen with Escape is not edited: it only exists while
        // in fullscreen, on top of the player's own keys.
        Assert.Equal(
            Enum.GetValues<HotkeyAction>().Where(a => a != HotkeyAction.LeaveFullscreen).Order(),
            placed.Order());
    }

    [Fact]
    public void Le_plein_ecran_ouvre_le_rangement_des_fenetres()
    {
        // Asked first by the player, 2026-10-17: it is the one used most.
        var arranging = HotkeyLayout.Sections.Single(s => s.Section == HotkeySection.Arranging);

        Assert.Equal([HotkeyAction.Fullscreen], arranging.Entries[0]);
    }

    [Fact]
    public void Les_trois_sections_dans_l_ordre_ou_on_s_en_sert()
    {
        Assert.Equal(
            [HotkeySection.Switching, HotkeySection.Arranging, HotkeySection.Application],
            HotkeyLayout.Sections.Select(s => s.Section));
    }

    [Fact]
    public void Les_comptes_et_les_tailles_tiennent_chacun_sur_une_ligne()
    {
        var series = HotkeyLayout.Sections
            .SelectMany(s => s.Entries.Select(e => (s.Section, Entry: e)))
            .Where(x => x.Entry.Count > 1)
            .ToList();

        Assert.Equal(2, series.Count);
        Assert.Equal(HotkeySection.Switching, series[0].Section);
        Assert.Equal(
            Enumerable.Range(0, 8).Select(i => HotkeyAction.Account1 + i),
            series[0].Entry);
        Assert.Equal(HotkeySection.Arranging, series[1].Section);
        Assert.Equal(
            [HotkeyAction.Size1, HotkeyAction.Size2, HotkeyAction.Size3, HotkeyAction.Size4],
            series[1].Entry);
    }

    [Fact]
    public void Une_serie_par_defaut_se_resume_par_sa_premiere_et_sa_derniere_touche()
    {
        var set = HotkeySet.Default;

        Assert.Equal("F1 … F8", HotkeyLayout.RunText(Series(set, HotkeyAction.Account1, 8)));
        Assert.Equal("Ctrl + 1 … Ctrl + 4", HotkeyLayout.RunText(Series(set, HotkeyAction.Size1, 4)));
    }

    [Fact]
    public void Une_touche_sortie_de_la_suite_ne_se_resume_plus()
    {
        // "F1 … F8" would then lie about the key that was changed.
        var bindings = Series(HotkeySet.Default, HotkeyAction.Account1, 8).ToList();
        bindings[2] = bindings[2] with { VirtualKey = VirtualKeys.D1 };

        Assert.Null(HotkeyLayout.RunText(bindings));
    }

    [Fact]
    public void Une_touche_retiree_ne_se_resume_plus()
    {
        var bindings = Series(HotkeySet.Default, HotkeyAction.Account1, 8).ToList();
        bindings[7] = bindings[7] with { VirtualKey = 0 };

        Assert.Null(HotkeyLayout.RunText(bindings));
    }

    private static IReadOnlyList<HotkeyBinding> Series(HotkeySet set, HotkeyAction first, int count) =>
        [.. Enumerable.Range(0, count).Select(i => set.For(first + i)!)];
}
