using DtHub.Core.Dofus;
using DtHub.Core.Scrcpy;

namespace DtHub.Core.Sessions;

/// <summary>Ce qu'on sait d'une session qui vient de s'éteindre.</summary>
/// <param name="Requested">Vrai si c'est nous qui avons demandé l'arrêt.</param>
/// <param name="EverRan">
/// Vrai si la session avait vraiment ouvert son afficheur et lancé le jeu. Un
/// échec d'ouverture est déjà traité pendant le lancement, par le repli à une
/// définition plus modeste ; il n'a rien à faire ici.
/// </param>
/// <param name="Failure">La nature du refus, telle que la sortie de scrcpy l'a dite.</param>
public readonly record struct SessionEnd(bool Requested, bool EverRan, ScrcpyFailureKind Failure);

/// <summary>Une fenêtre à rouvrir, et dans combien de temps.</summary>
public sealed record RecoveryRequest(DofusInstance Instance, TimeSpan Delay);

/// <summary>La suite à donner à une session éteinte.</summary>
public readonly record struct RecoveryDecision(bool Retry, TimeSpan Delay)
{
    /// <summary>On ne fait rien.</summary>
    public static readonly RecoveryDecision None = new(false, TimeSpan.Zero);
}

/// <summary>
/// Faut-il rouvrir une fenêtre de jeu qui s'est éteinte toute seule.
///
/// La question se pose parce que l'application ne survivait pas à la réponse :
/// quand la dernière session mourait sans qu'on l'ait demandé, DT Hub se
/// fermait. Or les déconnexions sont la première plainte des joueurs de DOFUS
/// Touch, et une liaison Wi-Fi qui hoquette n'est pas une raison de perdre sa
/// session de jeu et son application avec.
///
/// **La distinction qui compte est celle entre une fin voulue et une panne.**
/// Une fenêtre fermée à la main doit rester fermée : la rouvrir serait
/// exaspérant. Or de notre point de vue, les deux se ressemblent, aucun code à
/// nous n'ayant été appelé dans un cas comme dans l'autre. Ce qui les sépare
/// est que scrcpy sort proprement quand on ferme sa fenêtre, et en erreur
/// quand la liaison tombe. **On ne rouvre donc que sur un refus déclaré**, et
/// jamais sur une sortie propre.
///
/// Le reste des règles écarte ce qu'insister ne guérirait pas, dans le même
/// esprit que <see cref="ScrcpyOutputParser.CanRetrySmaller" /> : un appareil
/// non autorisé le restera, et scrcpy absent du poste ne s'installera pas tout
/// seul.
/// </summary>
public static class SessionRecovery
{
    /// <summary>
    /// Nombre de tentatives, au-delà duquel on cesse et on le dit.
    ///
    /// Trois, parce qu'un hoquet passager tient rarement plus de vingt
    /// secondes, et qu'une panne durable ne se résout pas en insistant. Une
    /// application qui rouvre indéfiniment une fenêtre qui retombe est pire
    /// qu'une application qui s'arrête : elle occupe sans servir.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Au bout de ce temps sans nouvelle panne, le compte des tentatives
    /// repart de zéro. Sans quoi une session de six heures finirait par
    /// épuiser son crédit sur des incidents sans rapport entre eux.
    /// </summary>
    public static readonly TimeSpan Forget = TimeSpan.FromMinutes(10);

    /// <summary>
    /// L'attente avant la tentative numéro <paramref name="attempt" />,
    /// comptée à partir de un.
    ///
    /// Croissante : la première panne mérite qu'on réessaie tout de suite, la
    /// troisième mérite qu'on laisse le Wi-Fi se remettre.
    /// </summary>
    public static TimeSpan DelayFor(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(5),
        _ => TimeSpan.FromSeconds(15),
    };

    /// <summary>
    /// Vrai si le refus a une chance de ne pas se reproduire.
    ///
    /// <see cref="ScrcpyFailureKind.None" /> en est exclu, et c'est le point
    /// important : aucun refus déclaré veut dire sortie propre, donc fenêtre
    /// fermée à la main.
    /// </summary>
    public static bool Recoverable(ScrcpyFailureKind kind) => kind
        is ScrcpyFailureKind.DeviceDisconnected
        or ScrcpyFailureKind.DeviceGone
        or ScrcpyFailureKind.ConnectionFailed
        or ScrcpyFailureKind.Unknown;

    /// <summary>
    /// La suite à donner.
    /// </summary>
    /// <param name="end">Ce qu'on sait de la fin.</param>
    /// <param name="attemptsAlready">
    /// Tentatives déjà faites pour cette instance dans la fenêtre de temps
    /// courante, zéro à la première panne.
    /// </param>
    public static RecoveryDecision Decide(SessionEnd end, int attemptsAlready)
    {
        if (end.Requested || !end.EverRan || !Recoverable(end.Failure) || attemptsAlready >= MaxAttempts)
        {
            return RecoveryDecision.None;
        }

        return new RecoveryDecision(true, DelayFor(attemptsAlready + 1));
    }
}
