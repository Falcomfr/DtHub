namespace DtHub.Core.Settings;

/// <summary>
/// Quel palier de qualité s'applique à quel compte.
///
/// Jusqu'ici il n'y en avait qu'un pour tout le monde. C'est le mauvais
/// réglage dans le cas qui nous occupe : on joue un compte et on en regarde
/// quatre. Le principal mérite soixante images et un gros débit ; les mules
/// n'en ont pas besoin, et ce qu'on leur épargne est autant de processeur, de
/// bande passante, de chaleur et de batterie en moins.
///
/// **La règle tient en une phrase : le compte l'emporte sur le réglage
/// commun.** Il n'y a pas de troisième niveau. Un profil de lancement paraît
/// en porter un, mais il recopie ses valeurs dans le réglage commun avant le
/// lancement : au moment où la question se pose, il ne reste que deux
/// sources.
///
/// **Les cadences ne suivent pas.** Un palier porte aussi le rythme des
/// sondages, et ceux-là sont propres à l'application, pas à une fenêtre :
/// interroger les appareils à cinq rythmes différents parce que cinq comptes
/// sont ouverts n'aurait aucun sens. Seuls la définition et le débit se
/// règlent par compte.
/// </summary>
public static class InstanceQuality
{
    /// <summary>
    /// Le palier qui s'applique, celui du compte s'il en a un, sinon le commun.
    /// </summary>
    public static StreamQuality Chosen(StreamQuality? instance, StreamQuality shared) => instance ?? shared;

    /// <summary>
    /// Le profil complet qui s'applique à un compte.
    /// </summary>
    /// <param name="instance">Palier du compte, ou <c>null</c> pour suivre le commun.</param>
    /// <param name="shared">Palier commun.</param>
    /// <param name="sharedCustom">Réglage fin commun.</param>
    /// <remarks>
    /// Il n'y a pas de réglage fin par compte, et c'est délibéré : ce serait un
    /// champ persisté que rien n'exposerait. Un compte choisit un palier parmi
    /// ceux qui existent, ou suit le commun. Les valeurs fines du palier
    /// personnalisé restent communes.
    /// </remarks>
    public static QualityProfile ProfileFor(
        StreamQuality? instance,
        StreamQuality shared,
        CustomQuality? sharedCustom) =>
        QualityProfile.For(Chosen(instance, shared), sharedCustom);
}
