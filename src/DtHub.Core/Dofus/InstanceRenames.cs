namespace DtHub.Core.Dofus;

/// <summary>Un compte dont la fenêtre est ouverte, et le nom qu'elle affiche.</summary>
/// <param name="Key">Clé de l'instance.</param>
/// <param name="UserName">Nom du profil Android, le nom de repli.</param>
/// <param name="Shown">Nom actuellement affiché sur l'onglet ou le titre.</param>
public readonly record struct OpenInstance(string Key, string UserName, string Shown);

/// <summary>
/// Ce qu'il faut réécrire quand un compte change de nom.
///
/// **Le défaut que ce type existe pour attraper.** Le libellé d'un onglet était
/// une copie du nom prise au moment où la fenêtre était logée dans le cadre, et
/// rien ne l'écrivait plus ensuite. Renommer un compte écrivait bien le réglage,
/// et la fenêtre gardait l'ancien nom jusqu'à sa réouverture. Deux comptes
/// renommés le même jour n'avaient pas le même sort : celui dont la fenêtre
/// avait été ouverte après son renommage paraissait juste, et faisait croire
/// que l'autre était un cas particulier.
///
/// **Pourquoi le calcul est ici et non dans le service qui pose les titres.**
/// C'est la partie qui décide, donc celle qu'on éprouve. Poser un titre sur une
/// fenêtre Windows ne s'éprouve pas ; savoir lequel, si.
///
/// **Et pourquoi il compare avant de rendre.** L'évènement des réglages se lève
/// à chaque écriture, dont la géométrie d'une fenêtre qu'on déplace, c'est à
/// dire souvent. Rendre tous les comptes à chaque fois ferait réécrire tous les
/// titres de toutes les fenêtres ouvertes pour un déplacement de souris.
/// </summary>
public static class InstanceRenames
{
    /// <summary>
    /// Les comptes ouverts dont le nom affiché ne correspond plus aux réglages,
    /// et le nom qu'ils doivent désormais porter.
    ///
    /// Rend un dictionnaire vide quand rien n'a bougé, ce qui est le cas
    /// ordinaire.
    /// </summary>
    /// <param name="open">Les comptes dont une fenêtre est ouverte.</param>
    /// <param name="customNameFor">
    /// Le nom choisi par l'utilisateur pour cette clé, tel que les réglages le
    /// portent maintenant, ou <c>null</c> s'il n'y en a pas.
    /// </param>
    public static Dictionary<string, string> Pending(
        IEnumerable<OpenInstance> open,
        Func<string, string?> customNameFor)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(customNameFor);

        var pending = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var instance in open)
        {
            var wanted = DofusInstance.NameOf(customNameFor(instance.Key), instance.UserName);

            if (!string.Equals(wanted, instance.Shown, StringComparison.Ordinal))
            {
                pending[instance.Key] = wanted;
            }
        }

        return pending;
    }
}
