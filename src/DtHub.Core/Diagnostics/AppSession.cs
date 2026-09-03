namespace DtHub.Core.Diagnostics;

/// <summary>
/// L'identifiant de ce lancement, porté par chaque ligne de journal.
///
/// Le gabarit n'en avait aucun, et quatre cent huit démarrages relevés en six
/// jours se mêlaient dans sept fichiers. Un rapport qui prend « les dernières
/// lignes » emportait donc les fautes de la veille, et celui qui prend « les
/// erreurs du fichier » en emportait des centaines sans rapport.
///
/// Six caractères suffisent : l'identifiant ne sert qu'à découper un fichier
/// d'une journée, non à distinguer deux machines. Il ne dit rien de personne :
/// il est tiré au hasard à chaque lancement et ne survit pas à la fermeture.
/// </summary>
public static class AppSession
{
    /// <summary>Longueur de l'identifiant.</summary>
    public const int Length = 6;

    /// <summary>L'identifiant de ce lancement.</summary>
    public static string Id { get; } = Mint();

    private static string Mint()
    {
        // Sans lettre ambiguë ni voyelle : un identifiant qu'on relit dans un
        // journal ne doit ni se confondre avec un mot ni se lire de travers.
        const string alphabet = "0123456789bcdfghjkmnpqrstvwxz";

        return string.Create(
            Length,
            alphabet,
            (span, source) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = source[Random.Shared.Next(source.Length)];
                }
            });
    }
}
