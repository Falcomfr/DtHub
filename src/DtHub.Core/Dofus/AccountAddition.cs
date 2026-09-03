namespace DtHub.Core.Dofus;

/// <summary>
/// Ce qu'a donné une tentative d'ajout de compte, prêt à être affiché.
/// </summary>
/// <param name="Succeeded">Vrai quand le profil est créé et le jeu prêt dedans.</param>
/// <param name="Message">Ce qu'on en dit à l'utilisateur, réussite comme échec.</param>
/// <param name="UserId">Le profil créé, ou -1 s'il n'y en a pas.</param>
public sealed record AccountAddition(bool Succeeded, string Message, int UserId = -1);
