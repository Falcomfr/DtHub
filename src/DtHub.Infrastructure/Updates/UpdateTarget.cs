namespace DtHub.Infrastructure.Updates;

/// <summary>
/// Ce que la mise à jour vise : la version qui tourne et le fichier qui la
/// porte.
///
/// Passés plutôt que devinés à l'exécution : c'est ce qui rend la pose de la
/// mise à jour éprouvable ailleurs que sur la machine de celui qui l'écrit.
/// </summary>
/// <param name="Running">La version en cours.</param>
/// <param name="ExecutablePath">Le chemin complet de l'exécutable en cours.</param>
public sealed record UpdateTarget(Version Running, string ExecutablePath);
