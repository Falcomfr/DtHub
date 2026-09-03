// Sonde de développement. Jamais employée par l'application.
//
// Elle interroge le vrai site et vérifie non pas des nombres figés, mais les
// suppositions dont l'application dépend. Deux défauts trouvés à l'œil et par
// hasard, les tanières à une seule étape et les raids à aucune, avaient vécu
// des semaines : ce sont eux qu'elle est faite pour attraper.
//
// Elle rend zéro si tout tient, un sinon. Le relevé de référence est
// reference.json, versionné à côté ; on le rebénit à la main avec --benir
// quand un écart est légitime.
//
// À lancer avant de livrer : dotnet run --project build/sonde-papycha
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using DtHub.Core.Papycha;
using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

var benir = args.Contains("--benir", StringComparer.Ordinal);
var reference = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "reference.json");

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("DtHub-sonde/1.0");

var client = new PapychaClient(http, NullLogger<PapychaClient>.Instance);
var constats = new List<Constat>();
var mesures = new Dictionary<string, int>(StringComparer.Ordinal);

Console.WriteLine("Lecture du site…\n");

// ---------------------------------------------------------------------------
// Ce que le client rend, par la voie que l'application emprunte.
// ---------------------------------------------------------------------------
var quetes = await client.GetQuestsAsync();
var rubriques = await client.GetSectionsAsync();
var pages = await client.GetPageSectionsAsync();
var lieux = await client.GetDungeonsAsync();
var chemins = await client.GetPathsAsync(
    [.. lieux.Where(d => d.Kind == DungeonKind.Dungeon).Select(d => d.Title)]);

mesures["quetes"] = quetes.Count;
mesures["rubriques"] = rubriques.Count;
mesures["pages-de-rubrique"] = pages.Count;
mesures["succes"] = pages.SelectMany(p => p.Groups).Select(g => g.Name).Distinct(StringComparer.Ordinal).Count();
mesures["donjons"] = lieux.Count(d => d.Kind == DungeonKind.Dungeon);
mesures["raids"] = lieux.Count(d => d.Kind == DungeonKind.Raid);
mesures["tanieres"] = lieux.Count(d => d.Kind == DungeonKind.Lair);
mesures["chemins"] = chemins.Count;
mesures["quetes-avec-niveau"] = quetes.Count(q => q.Level > 0);
mesures["quetes-avec-depart"] = quetes.Count(q => q.StartPosition.Length > 0);
mesures["quetes-avec-prerequis"] = quetes.Count(q => q.Prerequisites.Count > 0);
mesures["lieux-avec-niveau"] = lieux.Count(d => d.Level > 0);

// ---------------------------------------------------------------------------
// Les suppositions de forme, vérifiées sur les pages rendues.
// ---------------------------------------------------------------------------
var donjons = await Pages(6);
var raids = await Pages(741);
var tanieres = await Pages(721);
// Cent guides suffisent à dire si la forme des pages de quête a bougé : les
// sept cent quatre-vingt-deux sont écrites de la même main. Cent est aussi le
// maximum que le site accorde en une demande.
var guides = await Pages(7);

Exige(
    "chaque donjon porte un titre de second rang",
    donjons.Count(p => Titres(p, 2).Any(EstUneSection)),
    donjons.Count);

Exige(
    "chaque raid et chaque tanière porte un sommaire",
    raids.Concat(tanieres).Count(p => Sommaire(p).Count > 0),
    raids.Count + tanieres.Count);

// Ces deux-là ne sont pas des exigences mais des mesures : un donjon sur
// quatre-vingt-trois n'a pas le bloc d'en-tête, et six liens de sommaire sur
// quarante pointent une ancre absente. C'est l'état du site, pas un défaut de
// l'application, et l'application s'en accommode. Ce qui compte est que ces
// nombres ne se dégradent pas.
mesures["donjons-avec-bloc"] = donjons.Count(p => p.Contains("pcd-info", StringComparison.Ordinal));
mesures["ancres-de-sommaire-valides"] =
    raids.Concat(tanieres).Sum(p => Sommaire(p).Count(a => Ancres(p).Contains(a)));

Exige(
    "aucun guide de quête ne porte de sommaire",
    guides.Count(p => Sommaire(p).Count == 0),
    guides.Count);

Exige(
    "chaque guide de quête met ses consignes en évidence",
    guides.Count(p => p.Contains("<strong>", StringComparison.Ordinal)),
    guides.Count,
    tolerance: 0.9);

// La fenêtre des quêtes masque ce bandeau en entier, son propre bandeau le
// reprenant. Le jour où le site le renomme, le masquage devient muet et
// l'encart « Type : Principale » reparaît en tête de guide, à l'endroit même
// où la première étape est ancrée.
Exige(
    "chaque guide de quête porte le bandeau d'intro que la fenêtre masque",
    guides.Count(p => p.Contains("pqa-quest-intro", StringComparison.Ordinal)),
    guides.Count,
    tolerance: 0.9);

// Le bloc de progression, d'où viennent la quête précédente et la quête
// suivante. C'est la seule source qui franchisse la borne d'un succès : le jour
// où le site le renomme, les deux boutons se taisent sans que rien ne le dise.
Exige(
    "chaque guide de quête porte le bloc de progression du site",
    guides.Count(p => p.Contains("pqt-progress__column--next", StringComparison.Ordinal)),
    guides.Count,
    tolerance: 0.8);

// Combien de ces blocs nomment vraiment une quête suivante, et une seule. Une
// mesure et non une exigence : deux guides sur cinq n'ont pour suite qu'un
// succès validé, et c'est l'état du site. Elle passe par le code livré, pour
// que l'écart se voie ici et non à l'écran.
mesures["guides-avec-suivante"] =
    guides.Count(p => QuestPageParser.ParseChain(p).OnlyNextQuest is not null);

// L'annonce du départ écrite en prose, que le pont écarte de ses étapes parce
// que le bandeau la donne déjà : « La quête se lance en [2,-16] en parlant à
// Kerubim Crépin. » Une mesure et non une exigence : le jour où le site tourne
// la phrase autrement, l'exclusion ne mord plus et le nombre s'effondre.
mesures["annonces-de-depart-en-prose"] = guides.Sum(Annonces);

// ---------------------------------------------------------------------------
// Comparaison au relevé de référence.
// ---------------------------------------------------------------------------
var connu = File.Exists(reference)
    ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(reference))
        ?? []
    : [];

foreach (var (nom, valeur) in mesures.OrderBy(m => m.Key, StringComparer.Ordinal))
{
    if (!connu.TryGetValue(nom, out var avant))
    {
        constats.Add(new Constat(true, $"{nom} : {valeur} (nouveau relevé)"));

        continue;
    }

    // Une baisse est un signal : le site supprime rarement, l'application
    // cesse de lire souvent. Une hausse est la vie normale du site.
    var chute = avant > 0 && valeur < avant * 0.95;

    constats.Add(new Constat(
        !chute,
        $"{nom} : {valeur}" + (valeur == avant ? string.Empty : $" (référence {avant})")));
}

// ---------------------------------------------------------------------------
Console.WriteLine();

foreach (var constat in constats)
{
    Console.WriteLine((constat.Bon ? "  ok   " : "  ÉCART ") + constat.Texte);
}

var casse = constats.Count(c => !c.Bon);

if (benir)
{
    File.WriteAllText(
        reference,
        JsonSerializer.Serialize(mesures, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"\nRelevé écrit dans {Path.GetFullPath(reference)}");

    return 0;
}

Console.WriteLine(casse == 0
    ? "\nRien à signaler."
    : $"\n{casse} écart(s). Si le site a changé pour de bon, relancer avec --benir.");

return casse == 0 ? 0 : 1;

// ---------------------------------------------------------------------------

void Exige(string quoi, int obtenu, int attendu, double tolerance = 1.0)
{
    var bon = attendu == 0 || obtenu >= attendu * tolerance;

    constats.Add(new Constat(bon, $"{quoi} : {obtenu} / {attendu}"));
}

async Task<List<string>> Pages(int categorie, int limite = 100)
{
    var url = "https://papycha.fr/wp-json/wp/v2/posts"
        + $"?categories={categorie.ToString(CultureInfo.InvariantCulture)}"
        + $"&per_page={limite.ToString(CultureInfo.InvariantCulture)}&_fields=content";

    using var document = JsonDocument.Parse(await http.GetStringAsync(url));

    return
    [
        .. document.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("content").GetProperty("rendered").GetString() ?? string.Empty),
    ];
}

// Les paragraphes qui annoncent le départ de la quête, dans les termes que le
// pont écarte. La même expression que la sienne, ancrée en tête de paragraphe.
static int Annonces(string html) =>
    Regex.Matches(html, "<p\\b.*?</p>", RegexOptions.Singleline)
        .Select(m => Regex.Replace(Regex.Replace(m.Value, "<[^>]+>", " "), "\\s+", " ").Trim())
        .Count(texte => Regex.IsMatch(
            texte,
            @"^(La|Cette)\s+qu[eê]te\b[\s\S]{0,90}?\b(se\s+(lance|d[ée]clenche|d[ée]bloque)"
            + @"|est\s+(disponible|r[ée]p[ée]table|accessible))",
            RegexOptions.IgnoreCase));

static IEnumerable<string> Titres(string html, int rang) =>
    Regex.Matches(html, $"<h{rang}[^>]*>(.*?)</h{rang}>", RegexOptions.Singleline)
        .Select(m => Regex.Replace(m.Groups[1].Value, "<[^>]+>", string.Empty).Trim());

static bool EstUneSection(string titre) =>
    titre.Length > 0
    && !titre.StartsWith("Position du PNJ", StringComparison.OrdinalIgnoreCase)
    && !titre.StartsWith("Papycha remercie", StringComparison.OrdinalIgnoreCase);

static List<string> Sommaire(string html)
{
    var titre = Regex.Match(html, "<h[1-6][^>]*>\\s*Sommaire\\s*</h[1-6]>", RegexOptions.IgnoreCase);

    if (!titre.Success)
    {
        return [];
    }

    var suite = html[titre.Index..];
    var liste = Regex.Match(suite, "<ul.*?</ul>", RegexOptions.Singleline);

    return liste.Success
        ? [.. Regex.Matches(liste.Value, "href=\"#([^\"]*)\"").Select(m => Uri.UnescapeDataString(m.Groups[1].Value))]
        : [];
}

static HashSet<string> Ancres(string html) =>
    [.. Regex.Matches(html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value)];

internal sealed record Constat(bool Bon, string Texte);
