using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;

namespace DtHub.Tests.Sessions;

public class SessionRecoveryTests
{
    private static SessionEnd Panne(ScrcpyFailureKind kind = ScrcpyFailureKind.DeviceDisconnected) =>
        new(Requested: false, EverRan: true, Failure: kind);

    [Fact]
    public void Une_liaison_tombee_se_rouvre()
    {
        // Le cas qui justifie tout : le Wi-Fi hoquette, la fenêtre meurt, et
        // jusqu'ici l'application se fermait avec elle si c'était la dernière.
        var decision = SessionRecovery.Decide(Panne(), attemptsAlready: 0);

        Assert.True(decision.Retry);
        Assert.Equal(TimeSpan.FromSeconds(2), decision.Delay);
    }

    [Fact]
    public void Une_fenetre_fermee_a_la_main_reste_fermee()
    {
        // Le piège de cette fonction. Fermer la fenêtre à la croix n'appelle
        // aucun code à nous : de notre point de vue, ça ressemble à une panne.
        // Ce qui les sépare est que scrcpy sort proprement, donc sans refus
        // déclaré. Rouvrir ici serait exaspérant.
        var end = new SessionEnd(Requested: false, EverRan: true, Failure: ScrcpyFailureKind.None);

        Assert.False(SessionRecovery.Decide(end, attemptsAlready: 0).Retry);
    }

    [Fact]
    public void Un_arret_demande_ne_se_rouvre_pas()
    {
        var end = new SessionEnd(Requested: true, EverRan: true, Failure: ScrcpyFailureKind.DeviceDisconnected);

        Assert.False(SessionRecovery.Decide(end, attemptsAlready: 0).Retry);
    }

    [Fact]
    public void Une_session_qui_n_a_jamais_tourne_ne_se_rouvre_pas()
    {
        // L'échec d'ouverture est déjà traité pendant le lancement, par le
        // repli à une définition plus modeste. Le reprendre ici doublerait les
        // tentatives sans rien apporter.
        var end = new SessionEnd(Requested: false, EverRan: false, Failure: ScrcpyFailureKind.DeviceDisconnected);

        Assert.False(SessionRecovery.Decide(end, attemptsAlready: 0).Retry);
    }

    [Theory]
    [InlineData(ScrcpyFailureKind.DeviceDisconnected)]
    [InlineData(ScrcpyFailureKind.DeviceGone)]
    [InlineData(ScrcpyFailureKind.ConnectionFailed)]
    [InlineData(ScrcpyFailureKind.Unknown)]
    public void Les_pannes_de_liaison_se_rouvrent(ScrcpyFailureKind kind)
    {
        Assert.True(SessionRecovery.Recoverable(kind));
    }

    [Theory]
    [InlineData(ScrcpyFailureKind.None)]
    [InlineData(ScrcpyFailureKind.Unauthorized)]
    [InlineData(ScrcpyFailureKind.Environment)]
    [InlineData(ScrcpyFailureKind.Encoder)]
    [InlineData(ScrcpyFailureKind.VirtualDisplayRefused)]
    [InlineData(ScrcpyFailureKind.Timeout)]
    public void Ce_qu_insister_ne_guerirait_pas_ne_se_rouvre_pas(ScrcpyFailureKind kind)
    {
        // Un appareil non autorisé le restera, scrcpy absent ne s'installera
        // pas tout seul, et le refus d'encodeur a déjà son propre repli.
        Assert.False(SessionRecovery.Recoverable(kind));
    }

    [Fact]
    public void L_attente_grandit_a_chaque_tentative()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SessionRecovery.DelayFor(1));
        Assert.Equal(TimeSpan.FromSeconds(5), SessionRecovery.DelayFor(2));
        Assert.Equal(TimeSpan.FromSeconds(15), SessionRecovery.DelayFor(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Un_rang_de_tentative_absurde_prend_l_attente_la_plus_courte(int attempt)
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SessionRecovery.DelayFor(attempt));
    }

    [Fact]
    public void On_cesse_apres_trois_tentatives()
    {
        // Une application qui rouvre indéfiniment une fenêtre qui retombe est
        // pire qu'une application qui s'arrête : elle occupe sans servir.
        Assert.True(SessionRecovery.Decide(Panne(), attemptsAlready: 2).Retry);
        Assert.False(SessionRecovery.Decide(Panne(), attemptsAlready: 3).Retry);
        Assert.False(SessionRecovery.Decide(Panne(), attemptsAlready: 9).Retry);
    }

    [Fact]
    public void Les_trois_tentatives_s_espacent_comme_annonce()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SessionRecovery.Decide(Panne(), 0).Delay);
        Assert.Equal(TimeSpan.FromSeconds(5), SessionRecovery.Decide(Panne(), 1).Delay);
        Assert.Equal(TimeSpan.FromSeconds(15), SessionRecovery.Decide(Panne(), 2).Delay);
    }

    [Fact]
    public void Un_refus_ne_porte_aucune_attente()
    {
        Assert.Equal(TimeSpan.Zero, SessionRecovery.Decide(Panne(), attemptsAlready: 3).Delay);
        Assert.Equal(RecoveryDecision.None, SessionRecovery.Decide(Panne(), attemptsAlready: 3));
    }
}
