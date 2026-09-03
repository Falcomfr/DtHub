// Sonde de développement : ce que l'application propose comme quête voisine,
// comparé à ce que le site publie en pied d'article.
//
// Elle passe par le code livré, QuestNeighbourhood et QuestPageParser, et non
// par une réimplémentation qui pourrait se tromper d'accord avec elle-même.
//
//   dotnet.exe run --project build/sonde-voisines
//   dotnet.exe run --project build/sonde-voisines -- --lister
using System.Globalization;
using System.Text.Json;

using DtHub.Core.Papycha;
using DtHub.Core.Storage;
using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

var lister = args.Contains("--lister", StringComparer.Ordinal);
var cherche = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("DtHub-sonde/1.0");

var client = new PapychaClient(http, NullLogger<PapychaClient>.Instance);
var seed = new EmbeddedQuestSuccessSeed(NullLogger<EmbeddedQuestSuccessSeed>.Instance);

using var service = new QuestCatalogService(client, new Nulle(), seed);

var catalogue = await service.GetAsync(cancellationToken: CancellationToken.None);
var index = new QuestChainIndex(catalogue.Quests);

Console.WriteLine($"{catalogue.Quests.Count} quêtes au catalogue");

// Le contenu des pages, en huit requêtes plutôt qu'en sept cent quatre-vingts.
var pages = await Contents(7);

Console.WriteLine($"{pages.Count} pages relevées");

int gagneSuivante = 0, gagnePrecedente = 0;
int changeSuivante = 0, changePrecedente = 0;
int muetCatalogue = 0, muetSuivante = 0, ambigu = 0;
List<string> ecarts = [];

foreach (var quest in catalogue.Quests)
{
    var voisines = QuestNeighbourhood.Of(quest, catalogue.Quests, index);

    if (cherche is not null
        && quest.Title.Contains(cherche, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine();
        Console.WriteLine($"  {quest.Title}   [succès {quest.SuccessName}]");
        Console.WriteLine($"    précédente : {voisines.Previous?.Title ?? "(aucune)"}");
        Console.WriteLine($"    suivante   : {voisines.Next?.Title ?? "(aucune)"}");
    }

    if (voisines.Next is null)
    {
        muetCatalogue++;
    }

    if (!pages.TryGetValue(Key(quest.Url), out var html))
    {
        if (voisines.Next is null)
        {
            muetSuivante++;
        }

        continue;
    }

    var chain = QuestPageParser.ParseChain(html);

    Compare("suivante", voisines.Next?.Url, Only(chain.Next), ref gagneSuivante, ref changeSuivante);
    Compare(
        "précédente", voisines.Previous?.Url, Only(chain.Previous),
        ref gagnePrecedente, ref changePrecedente);

    if (voisines.Next is null && Only(chain.Next) is null)
    {
        muetSuivante++;

        if (chain.Next.Count(l => l.Kind == QuestLinkKind.Quest) > 1)
        {
            ambigu++;
        }
    }

    void Compare(string sens, string? notre, string? site, ref int gagne, ref int change)
    {
        if (site is null)
        {
            return;
        }

        if (notre is null)
        {
            gagne++;
        }
        else if (!string.Equals(Key(notre), Key(site), StringComparison.OrdinalIgnoreCase))
        {
            change++;

            if (lister)
            {
                ecarts.Add($"{quest.Title} [{sens}]\n    nous : {notre}\n    site : {site}");
            }
        }
    }
}

Console.WriteLine();
Console.WriteLine($"suivantes gagnées    : {gagneSuivante}");
Console.WriteLine($"suivantes changées   : {changeSuivante}");
Console.WriteLine($"précédentes gagnées  : {gagnePrecedente}");
Console.WriteLine($"précédentes changées : {changePrecedente}");
// Le chiffre qui compte : un succès dont la dernière quête ne mène nulle part
// laisse le lecteur en plan au moment précis où il finit une série.
var culsDeSac = 0;
var ramifies = 0;
var muets = 0;
var succes = catalogue.Quests
    .Where(q => q.SuccessName.Length > 0)
    .GroupBy(q => q.SuccessName, StringComparer.Ordinal)
    .ToList();

foreach (var groupe in succes)
{
    var derniere = QuestPlayOrder.Sorted(groupe)[^1];
    var apres = QuestNeighbourhood.Of(derniere, catalogue.Quests, index).Next;

    if (apres is null
        && pages.TryGetValue(Key(derniere.Url), out var fin))
    {
        apres = QuestPageParser.ParseChain(fin).OnlyNextQuest is { } publiee
            ? derniere with { Url = publiee.Url }
            : null;
    }

    if (apres is null)
    {
        culsDeSac++;

        if (pages.TryGetValue(Key(derniere.Url), out var page))
        {
            var suites = QuestPageParser.ParseChain(page).Next
                .Count(l => l.Kind == QuestLinkKind.Quest);

            if (suites > 1)
            {
                ramifies++;
            }
            else
            {
                muets++;
            }
        }
        else
        {
            muets++;
        }
    }
}

Console.WriteLine();
Console.WriteLine($"succès sans suite : {culsDeSac} / {succes.Count}");
Console.WriteLine($"   dont le site ramifie   : {ramifies}");
Console.WriteLine($"   dont le site ne dit rien : {muets}");
Console.WriteLine($"sans suivante, catalogue seul : {muetCatalogue}");
Console.WriteLine($"sans suivante, site compris   : {muetSuivante}   (dont {ambigu} que le site ramifie)");

if (lister)
{
    Console.WriteLine();

    foreach (var ecart in ecarts.Order(StringComparer.CurrentCulture))
    {
        Console.WriteLine(ecart);
    }
}

// ---------------------------------------------------------------------------
// L'ordre des listes : un prérequis doit paraître avant la quête qui le réclame.
//
// La liste dit la progression. Une quête placée avant ce qu'elle exige la
// dément, et l'on ne sait plus si l'on peut la prendre.

var listes = 0;
var fautes = 0;
var boucles = 0;
List<string> details = [];

foreach (var section in catalogue.Sections)
{
    var dedans = catalogue.Quests.Where(q => q.SectionIds.Contains(section.Id)).ToList();

    if (dedans.Count == 0)
    {
        continue;
    }

    listes++;

    var plan = QuestZonePlan.Of(
        [.. dedans.OrderBy(q => q.Title, StringComparer.CurrentCulture)],
        catalogue.SuccessOrder);

    List<QuestSummary> rangees = [.. plan.SelectMany(b => b.Quests)];

    Dictionary<string, int> place = new(StringComparer.Ordinal);
    Dictionary<string, QuestSummary> parTitre = new(StringComparer.Ordinal);
    Dictionary<string, QuestSummary> dernierDuSucces = new(StringComparer.Ordinal);

    for (var i = 0; i < rangees.Count; i++)
    {
        place[rangees[i].Url] = i;
        parTitre.TryAdd(QuestSearch.Normalize(rangees[i].Title), rangees[i]);
    }

    foreach (var bloc in plan.Where(b => b.IsSuccess))
    {
        dernierDuSucces[QuestSearch.Normalize(bloc.SuccessName)] = bloc.Quests[^1];
    }

    if (cherche is not null)
    {
        for (var i = 0; i < rangees.Count; i++)
        {
            if (!rangees[i].Title.Contains(cherche, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var avantElle = i > 0 ? rangees[i - 1].Title : "(rien)";
            var apresElle = i + 1 < rangees.Count ? rangees[i + 1].Title : "(rien)";

            Console.WriteLine();
            Console.WriteLine($"  {section.Name} : rang {i + 1} / {rangees.Count}");
            Console.WriteLine($"    « {rangees[i].Title} »   [succès {rangees[i].SuccessName}]");
            Console.WriteLine($"    juste avant : {avantElle}");
            Console.WriteLine($"    juste après : {apresElle}");

            if (lister)
            {
                Console.WriteLine();

                var rang = 0;

                foreach (var bloc in plan)
                {
                    var nom = bloc.IsSuccess ? $"[{bloc.SuccessName}]" : "(seule)";

                    foreach (var membre in bloc.Quests)
                    {
                        rang++;
                        Console.WriteLine($"    {rang,3}. {nom,-40} {membre.Title}");
                    }
                }
            }
        }
    }

    // Le graphe des blocs, refait ici pour dire si une faute vient d'une boucle
    // que le rangement ne peut pas trancher, un succès étant insécable.
    Dictionary<string, int> blocDe = new(StringComparer.Ordinal);

    for (var b = 0; b < plan.Count; b++)
    {
        foreach (var membre in plan[b].Quests)
        {
            blocDe[membre.Url] = b;
        }
    }

    List<HashSet<int>> apres = [.. Enumerable.Range(0, plan.Count).Select(_ => new HashSet<int>())];

    foreach (var quest in rangees)
    {
        foreach (var need in quest.Prerequisites)
        {
            var nom = PrerequisiteLabel.Of(need);
            var k = QuestSearch.Normalize(nom.Name);
            var source = nom.IsSuccess ? dernierDuSucces.GetValueOrDefault(k) : parTitre.GetValueOrDefault(k);

            if (source is not null && blocDe[source.Url] != blocDe[quest.Url])
            {
                apres[blocDe[source.Url]].Add(blocDe[quest.Url]);
            }
        }
    }

    bool Atteint(int depuis, int vers)
    {
        HashSet<int> vus = [depuis];
        Queue<int> file = new([depuis]);

        while (file.Count > 0)
        {
            foreach (var suivant in apres[file.Dequeue()])
            {
                if (suivant == vers)
                {
                    return true;
                }

                if (vus.Add(suivant))
                {
                    file.Enqueue(suivant);
                }
            }
        }

        return false;
    }

    foreach (var quest in rangees)
    {
        foreach (var need in quest.Prerequisites)
        {
            var nomme = PrerequisiteLabel.Of(need);
            var cle = QuestSearch.Normalize(nomme.Name);

            var avant = nomme.IsSuccess
                ? dernierDuSucces.GetValueOrDefault(cle)
                : parTitre.GetValueOrDefault(cle);

            if (avant is null || ReferenceEquals(avant, quest))
            {
                continue;
            }

            if (place[avant.Url] > place[quest.Url])
            {
                fautes++;

                var boucle = Atteint(blocDe[quest.Url], blocDe[avant.Url]);

                if (boucle)
                {
                    boucles++;
                }

                if (lister)
                {
                    details.Add(
                        $"{section.Name} : « {quest.Title} » avant son prérequis "
                        + $"« {avant.Title} »   {(boucle ? "BOUCLE" : "autre")}");
                }
            }
        }
    }
}

Console.WriteLine();
Console.WriteLine($"listes rangées : {listes}");
Console.WriteLine($"prérequis placés après la quête qui les réclame : {fautes}   (dont {boucles} en boucle)");

foreach (var detail in details.Order(StringComparer.CurrentCulture))
{
    Console.WriteLine($"   {detail}");
}

// La quête nommée par une colonne, s'il n'y en a qu'une : en désigner une
// parmi plusieurs mentirait sur ce que le site publie.
static string? Only(IReadOnlyList<QuestLink> links)
{
    string? seul = null;

    foreach (var link in links)
    {
        if (link.Kind != QuestLinkKind.Quest)
        {
            continue;
        }

        if (seul is not null)
        {
            return null;
        }

        seul = link.Url;
    }

    return seul;
}

static string Key(string url) => url.TrimEnd('/').ToLowerInvariant();

async Task<Dictionary<string, string>> Contents(int categorie)
{
    Dictionary<string, string> pages = new(StringComparer.OrdinalIgnoreCase);

    for (var page = 1; page <= 12; page++)
    {
        var url = "https://papycha.fr/wp-json/wp/v2/posts"
            + string.Create(
                CultureInfo.InvariantCulture,
                $"?categories={categorie}&per_page=100&page={page}&_fields=link,content");

        using var response = await http.GetAsync(new Uri(url), CancellationToken.None);

        if (!response.IsSuccessStatusCode)
        {
            break;
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));

        var count = 0;

        foreach (var post in document.RootElement.EnumerateArray())
        {
            count++;

            var link = post.GetProperty("link").GetString();
            var content = post.GetProperty("content").GetProperty("rendered").GetString();

            if (link is not null && content is not null)
            {
                pages[Key(link)] = content;
            }
        }

        if (count < 100)
        {
            break;
        }
    }

    return pages;
}

internal sealed class Nulle : IDocumentStore<QuestCatalogDocument>
{
    public string FilePath => "(mémoire)";

    public Task<QuestCatalogDocument> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new QuestCatalogDocument());

    public Task SaveAsync(QuestCatalogDocument document, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
