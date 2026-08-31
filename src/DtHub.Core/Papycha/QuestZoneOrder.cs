namespace DtHub.Core.Papycha;

/// <summary>
/// Nom d'affichage et ordre de parcours des zones de quêtes.
///
/// Le site nomme et range ses rubriques pour un lecteur qui arrive par un
/// moteur de recherche : « Quêtes du Port de Madrestam » se lit bien dans une
/// page, mal dans une liste où toutes les lignes sont des quêtes. Et son ordre
/// est celui de son propre tableau, qui ne suit pas la progression du jeu.
///
/// Fonctions pures : elles se vérifient sans réseau ni catalogue.
/// </summary>
public static class QuestZoneOrder
{
    /// <summary>
    /// Zones du monde, dans l'ordre où l'on y joue. Les quêtes principales
    /// ouvrent la marche, puis la progression géographique d'Albuera à Frigost.
    /// </summary>
    private static readonly string[] Progression =
    [
        "Quêtes principales",
        "Albuera",
        "Astrub",
        "Amakna",
        "Château d'Amakna",
        "Port de Madrestam",
        "Bworks",
        "Île des Wabbits",
        "Bonta & Cania",
        "Île d'Otomaï",
        "Île de Pandala",
        "Sufokia",
        "Île d'Orado",
        "Archipel de Vulkania",
        "Îlot Rifique",
        "Île de Frigost",
    ];

    /// <summary>
    /// Ce qui ne relève pas de la progression : les chemins d'alignement, les
    /// contenus saisonniers, et ce qu'aucune zone ne réclame. Rangé après un
    /// intertitre pour que la liste ne mélange pas deux choses.
    /// </summary>
    private static readonly string[] Extras =
    [
        "Krosmoz",
        "Île de Nowel",
        "Sain Ballotin",
        "Bulles Temporelles",
        "Dédale",
        "[Alignement] Bontarien",
        "[Alignement] Brâkmarien",
        "Quêtes répétables",
        "Autres quêtes",
    ];

    /// <summary>Intertitre qui sépare la progression du reste.</summary>
    public const string ExtrasHeader = "Quêtes supplémentaires";

    /// <summary>
    /// Rang d'une zone dans la liste, ou un rang de fin si on ne la connaît pas.
    ///
    /// Une zone inconnue se range à la fin de la progression et non au milieu :
    /// le site peut en ajouter, et une nouveauté doit se voir sans dérégler ce
    /// qui la précède.
    /// </summary>
    public static int RankOf(string? name)
    {
        var key = QuestSearch.Normalize(DisplayName(name));

        return key.Length > 0 && Ranks.TryGetValue(key, out var rank) ? rank : UnknownRank;
    }

    /// <summary>Rang donné aux zones que la table ne nomme pas.</summary>
    public static int UnknownRank => Progression.Length;

    /// <summary>Vrai si la zone appartient au bloc qui suit l'intertitre.</summary>
    public static bool IsExtra(string? name) => RankOf(name) > UnknownRank;

    /// <summary>
    /// Nom tel qu'on veut le lire dans la liste.
    ///
    /// Le préfixe « Quêtes » ne disparaît que s'il est suivi d'un article : ce
    /// qui reste est alors un lieu, « Quêtes du Port de Madrestam » donnant
    /// « Port de Madrestam ». Sans article, le mot fait partie du nom et reste :
    /// « Quêtes principales » et « Quêtes répétables » ne désignent pas un
    /// endroit mais une sorte de quête.
    /// </summary>
    public static string DisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var value = name.Trim();

        foreach (var article in Articles)
        {
            var prefix = "Quêtes " + article;

            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rest = value[prefix.Length..].TrimStart('\'', '’', ' ');

                if (rest.Length > 0)
                {
                    return rest;
                }
            }
        }

        return value;
    }

    /// <summary>
    /// Articles reconnus, du plus long au plus court : « de la » doit être
    /// essayé avant « de », sans quoi « Quêtes de la Sain Ballotin » donnerait
    /// « la Sain Ballotin ».
    /// </summary>
    private static readonly string[] Articles =
    [
        "de la ",
        "de l",
        "du ",
        "des ",
        "de ",
        "d",
    ];

    /// <summary>
    /// Déclaré après <see cref="Articles"/> : les champs statiques s'initialisent
    /// dans l'ordre du fichier, et construire la table avant de connaître les
    /// articles la faisait échouer sur une référence nulle.
    /// </summary>
    private static readonly Dictionary<string, int> Ranks = BuildRanks();

    private static Dictionary<string, int> BuildRanks()
    {
        Dictionary<string, int> ranks = new(StringComparer.Ordinal);

        for (var i = 0; i < Progression.Length; i++)
        {
            ranks[QuestSearch.Normalize(DisplayName(Progression[i]))] = i;
        }

        // Après le rang réservé aux zones inconnues, pour que celles-ci restent
        // dans la progression plutôt que de tomber dans le supplément.
        for (var i = 0; i < Extras.Length; i++)
        {
            ranks[QuestSearch.Normalize(DisplayName(Extras[i]))] = Progression.Length + 1 + i;
        }

        return ranks;
    }
}
