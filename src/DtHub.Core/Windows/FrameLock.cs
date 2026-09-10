namespace DtHub.Core.Windows;

/// <summary>
/// Dit si le cadre à onglets doit rester où il est.
///
/// Le cadenas d'un compte promet que sa fenêtre « ne bougera plus lors d'un
/// empilement, d'une mise côte à côte ou d'un changement de taille ». Logé dans
/// le cadre, un compte n'a plus de géométrie à lui : c'est le cadre qui
/// commande, et le redimensionner redimensionne forcément tout ce qu'il loge.
/// Le verrou n'y protégeait donc de rien, alors que les deux bascules sont
/// indépendantes et qu'un compte peut très bien être verrouillé et logé.
///
/// La règle retenue est celle qui tient la promesse : **un seul compte logé
/// verrouillé fige le cadre entier**. Un verrou est une protection, et un
/// voisin ne lève pas la protection d'un autre. Le prix est assumé : un seul
/// cadenas immobilise le cadre de tous ceux qui s'y trouvent.
/// </summary>
public static class FrameLock
{
    /// <summary>
    /// Vrai si le cadre doit être laissé en place par les commandes de
    /// géométrie.
    /// </summary>
    /// <param name="locked">Clefs des comptes dont la fenêtre est verrouillée.</param>
    /// <param name="tabbed">Clefs des comptes logés dans le cadre.</param>
    public static bool Freezes(IEnumerable<string> locked, IEnumerable<string> tabbed)
    {
        ArgumentNullException.ThrowIfNull(locked);
        ArgumentNullException.ThrowIfNull(tabbed);

        var verrouilles = locked as ISet<string> ?? new HashSet<string>(locked, StringComparer.Ordinal);

        return tabbed.Any(verrouilles.Contains);
    }
}
