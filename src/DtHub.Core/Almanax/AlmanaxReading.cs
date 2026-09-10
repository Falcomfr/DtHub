namespace DtHub.Core.Almanax;

/// <summary>
/// Lire la page de l'Almanax sans dépendre d'une langue.
///
/// Le portail publie en huit langues et l'application en parle trois. Trois
/// tournures relevées le 10 septembre 2026 sur la même journée :
///
/// <code>
/// fr  Récupérer 1 Dent de Dragodinde et rapporter l'offrande à Théodoran Ax
/// en  Find 1 Dragoturkey Tooth and take the offering to Antyklime Ax
/// es  Recolectar 1 Diente de dragopavo y llevárselo a Ontoral Zo
/// </code>
///
/// Le verbe change, le personnage change, et jusqu'à son nom. Ce qui ne change
/// pas est la forme : un nombre, puis l'objet, puis une conjonction. C'est sur
/// cette forme qu'on lit, et non sur des mots à énumérer langue par langue.
/// </summary>
public static class AlmanaxReading
{
    /// <summary>
    /// Les conjonctions qui closent le nom de l'objet, dans les langues que
    /// l'application sert.
    /// </summary>
    private static readonly string[] Connectors = [" et ", " and ", " y "];

    /// <summary>
    /// Vrai quand le bloc lu est bien celui de DOFUS Touch.
    ///
    /// C'est le garde-fou de toute la fonction. Le portail sert les deux jeux
    /// sur la même page, et l'Almanax de DOFUS demande d'autres objets : rien
    /// à l'écran ne dirait qu'on montre le mauvais. Le titre du bloc porte le
    /// nom du jeu dans les trois langues, à des places différentes :
    ///
    /// <code>
    /// fr  Bonus et Quêtes DOFUS Touch
    /// en  DOFUS Touch bonuses and quests
    /// es  Bonus y misiones DOFUS Touch
    /// </code>
    ///
    /// On cherche donc le nom n'importe où, et on refuse tout le reste. Mieux
    /// vaut ne rien montrer que montrer l'offrande d'un autre jeu.
    /// </summary>
    public static bool IsTouch(string? heading) =>
        (heading ?? string.Empty).Contains("DOFUS Touch", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ce qui suit le deux-points, ou le texte entier s'il n'y en a pas.
    ///
    /// Le portail préfixe ses intitulés, « Bonus : », « Bonus: », « Quête : »,
    /// « Quest: », « Misión: ». Le mot change, la ponctuation aussi, mais le
    /// deux-points est là dans les trois langues, et il n'apparaît pas dans les
    /// valeurs elles-mêmes.
    /// </summary>
    public static string AfterColon(string? text)
    {
        var whole = Clean(text);
        var colon = whole.IndexOf(':', StringComparison.Ordinal);

        return colon < 0 ? whole : whole[(colon + 1)..].Trim();
    }

    /// <summary>
    /// Le nombre et l'objet à rapporter, ou <c>null</c> si la phrase ne se
    /// laisse pas lire.
    ///
    /// Rendre <c>null</c> n'est pas un échec : la fenêtre affiche alors la
    /// phrase entière, qui dit déjà tout. On ne perd que la mise en avant.
    /// </summary>
    public static (int Quantity, string Item)? Offering(string? sentence)
    {
        var whole = Clean(sentence);

        // Le premier nombre de la phrase : les verbes le précèdent tous, et
        // aucun nom d'objet ne commence par un chiffre.
        var start = -1;
        var end = -1;

        for (var i = 0; i < whole.Length; i++)
        {
            if (!char.IsAsciiDigit(whole[i]))
            {
                continue;
            }

            start = i;
            end = i;

            while (end + 1 < whole.Length && char.IsAsciiDigit(whole[end + 1]))
            {
                end++;
            }

            break;
        }

        if (start < 0 || !int.TryParse(whole[start..(end + 1)], out var quantity))
        {
            return null;
        }

        var rest = whole[(end + 1)..];

        // La conjonction la plus proche, et non la première de la liste : un
        // nom d'objet peut contenir « y » ou « et » d'une autre langue.
        var cut = rest.Length;

        foreach (var connector in Connectors)
        {
            var at = rest.IndexOf(connector, StringComparison.OrdinalIgnoreCase);

            if (at >= 0 && at < cut)
            {
                cut = at;
            }
        }

        var item = rest[..cut].Trim();

        return item.Length == 0 ? null : (quantity, item);
    }

    /// <summary>
    /// Le texte débarrassé des blancs du gabarit : la page rend ses
    /// valeurs sur plusieurs lignes, indentées, et le texte brut arrive
    /// criblé d'espaces et de sauts.
    /// </summary>
    public static string Clean(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
