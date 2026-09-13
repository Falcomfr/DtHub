// Development probe. Never used by the application.
//
// It queries the real site and checks not fixed numbers, but the
// assumptions the application depends on. Two flaws found by eye and
// by chance, lairs with only one step and raids with none, had lived
// for weeks: these are what it is built to catch.
//
// It returns zero if everything holds, one otherwise. The reference
// reading is reference.json, versioned alongside; it is re-blessed by
// hand with --benir when a discrepancy is legitimate.
//
// To run before shipping: dotnet run --project build/sonde-papycha
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
// What the client returns, through the path the application uses.
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
// The shape assumptions, checked on the rendered pages.
// ---------------------------------------------------------------------------
var donjons = await Pages(6);
var raids = await Pages(741);
var tanieres = await Pages(721);
// A hundred guides are enough to say whether the shape of quest pages
// has moved: the seven hundred eighty-two are written by the same
// hand. A hundred is also the maximum the site grants in one request.
var guides = await Pages(7);

Exige(
    "chaque donjon porte un titre de second rang",
    donjons.Count(p => Titres(p, 2).Any(EstUneSection)),
    donjons.Count);

Exige(
    "chaque raid et chaque tanière porte un sommaire",
    raids.Concat(tanieres).Count(p => Sommaire(p).Count > 0),
    raids.Count + tanieres.Count);

// These two are not requirements but measurements: one dungeon out of
// eighty-three does not have the header block, and six summary links
// out of forty point to a missing anchor. This is the state of the
// site, not a flaw of the application, and the application copes with
// it. What matters is that these numbers do not get worse.
mesures["donjons-avec-bloc"] = donjons.Count(p => p.Contains("pcd-info", StringComparison.Ordinal));
mesures["ancres-de-sommaire-valides"] =
    raids.Concat(tanieres).Sum(p => Sommaire(p).Count(a => Ancres(p).Contains(a)));

Exige(
    "aucun guide de quête ne porte de sommaire",
    guides.Count(p => Sommaire(p).Count == 0),
    guides.Count);

// The quest window hides this banner entirely, replacing it with its
// own banner. The day the site renames it, the hiding goes silent and
// the "Type : Principale" box reappears at the top of the guide, at
// the very spot where the first step is anchored.
Exige(
    "chaque guide de quête porte le bandeau d'intro que la fenêtre masque",
    guides.Count(p => p.Contains("pqa-quest-intro", StringComparison.Ordinal)),
    guides.Count,
    tolerance: 0.9);

// The progress block. **What this check protects has grown.** It used
// to carry the previous quest and the next quest from the window
// footer, the only source that crosses the boundary of an achievement.
// It is now also shown as is in the guide, the footer being able to
// announce only one follow-up where the site often names several. The
// day the site renames it, it will not only be two buttons that fall
// silent: the end of the guide will say nothing at all anymore.
Exige(
    "chaque guide de quête porte le bloc de progression du site",
    guides.Count(p => p.Contains("pqt-progress__column--next", StringComparison.Ordinal)),
    guides.Count,
    tolerance: 0.8);

// How many of these blocks truly name a next quest, and only one. A
// measurement and not a requirement: two guides out of five have only
// a validated achievement as their follow-up, and that is the state of
// the site. It goes through the shipped code, so that the discrepancy
// shows here and not on screen.
mesures["guides-avec-suivante"] =
    guides.Count(p => QuestPageParser.ParseChain(p).OnlyNextQuest is not null);

// What the bridge spots as instructions. This is the number that says
// whether the rule still bites: it now rests only on grammar, the
// imperative of the second person plural or on coordinates, with noise
// set aside. The day the site turns its instructions another way, this
// number will collapse and it is here that it will be seen, rather
// than on an empty guide on screen.
//
// Two measurements and not requirements: thirty-one guides out of the
// 782 truly have no instruction at all in the imperative, repeatable
// or gathering quests of three to nine paragraphs, and that is the
// state of the site.
//
// The rule is restated here, as the departure announcement is already
// restated just below: the bridge lives in JavaScript, this probe in
// C#, and nothing can link them. The restatement is therefore
// deliberately approximate, and is enough for a watch. The exact
// reading is redone with build/sonde-papycha/audit-etapes.mjs, which
// runs the bridge's own code on the 782 guides.
mesures["consignes-reperees"] = guides.Sum(Consigne.Dans);
mesures["guides-avec-consigne"] = guides.Count(p => Consigne.Dans(p) > 0);

// The departure announcement written in prose, which the bridge
// excludes from its steps because the banner already gives it:
// "La quête se lance en [2,-16] en parlant à Kerubim Crépin." A
// measurement and not a requirement: the day the site turns the
// sentence another way, the exclusion no longer bites and the number
// collapses.
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

    // A drop is a signal: the site rarely removes, the application often
    // stops reading. A rise is the site's normal life.
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

// The paragraphs that announce the quest's departure, in the terms
// the bridge excludes. The same expression as its own, anchored at the
// start of the paragraph.
static int Annonces(string html) =>
    Regex.Matches(html, "<p\\b.*?</p>", RegexOptions.Singleline)
        .Select(Consigne.Texte)
        .Count(Consigne.Depart.IsMatch);

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

/// <summary>
/// What the bridge keeps as an instruction, restated here so it can be
/// counted.
///
/// Deliberately coarser than it: paragraphs are captured by the
/// regular expression and not by the place they occupy in the
/// document, so that a nested paragraph counts here while the bridge
/// ignores it. This is a watch, not a measurement: what matters is
/// that the number does not collapse the day the site changes the way
/// it writes.
/// </summary>
internal static class Consigne
{
    // Imperative verbs that their ending does not betray.
    private static readonly string[] Irreguliers =
        ["faites", "dites", "soyez", "ayez", "sachez", "veuillez"];

    // Words after which an "-ez" is a present tense and not a command.
    private static readonly string[] Sujets = ["vous", "ne", "n", "qui", "que", "qu", "et"];

    // Words ending in "-ez" that order nothing: nouns, and the futures
    // of "avoir" and "être", whose imperatives already appear among the
    // irregulars.
    private static readonly string[] FauxAmis = ["chez", "assez", "nez", "rez", "aurez", "serez"];

    private static readonly Regex Paragraphe = new("<p\\b.*?</p>", RegexOptions.Singleline);

    private static readonly Regex Etiquette = new(
        @"^\s*(pr[ée].?requis|source|plage habituelle|dur[ée]e|note|notes|attention|astuce"
        + @"|remarque|rappel|important|info|informations?)\s*:",
        RegexOptions.IgnoreCase);

    private static readonly Regex Coordonnees = new(@"\[\s*-?\d+\s*,\s*-?\d+\s*\]");

    private static readonly Regex Mots = new(@"[\p{L}\p{M}]+");

    /// <summary>How many paragraphs on this page give a command.</summary>
    internal static int Dans(string html) =>
        Paragraphe.Matches(html).Select(Texte).Count(EstUneConsigne);

    internal static string Texte(Match paragraphe) =>
        Regex.Replace(Regex.Replace(paragraphe.Value, "<[^>]+>", " "), "\\s+", " ").Trim();

    private static bool EstUneConsigne(string texte) =>
        texte.Length > 0
        && !EstDuBruit(texte)
        && (Coordonnees.IsMatch(texte) || Ordonne(texte));

    private static bool EstDuBruit(string texte)
    {
        if (Etiquette.IsMatch(texte))
        {
            return true;
        }

        if (Depart.IsMatch(texte) && !Ordonne(texte))
        {
            return true;
        }

        if (texte.Length <= 45 && texte.EndsWith(':'))
        {
            return true;
        }

        return texte.StartsWith('(') && texte.EndsWith(')');
    }

    /// <summary>
    /// The departure announcement, which the banner already gives and
    /// which the guide repeats in prose. The same expression as the
    /// bridge's, anchored at the start.
    /// </summary>
    internal static readonly Regex Depart = new(
        @"^(la|cette)\s+qu[eê]te\b[\s\S]{0,90}?\b(se\s+(lance|d[ée]clenche|d[ée]bloque)"
        + @"|est\s+(disponible|r[ée]p[ée]table|accessible))",
        RegexOptions.IgnoreCase);

    private static bool Ordonne(string texte)
    {
        // The splitting must be Unicode: "\W" only knows ASCII, and
        // "Protégez" becomes "Prot" and "gez" there.
        var mots = Mots.Matches(texte).Select(m => m.Value.ToLowerInvariant()).ToList();

        for (var i = 0; i < mots.Count; i++)
        {
            if (i > 0 && Sujets.Contains(mots[i - 1], StringComparer.Ordinal))
            {
                continue;
            }

            if (Irreguliers.Contains(mots[i], StringComparer.Ordinal))
            {
                return true;
            }

            // Four letters and not five: "Tuez" makes four of them.
            if (mots[i].Length >= 4
                && mots[i].EndsWith("ez", StringComparison.Ordinal)
                && !FauxAmis.Contains(mots[i], StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
