// Sonde de développement : combien de succès le site range vraiment.
//
// Elle passe par le service, donc par le code livré, et non par une
// réimplémentation qui pourrait se tromper d'accord avec elle-même.
using DtHub.Core.Papycha;
using DtHub.Core.Storage;
using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("DtHub-sonde/1.0");

var client = new PapychaClient(http, NullLogger<PapychaClient>.Instance);
var seed = new EmbeddedQuestSuccessSeed(NullLogger<EmbeddedQuestSuccessSeed>.Instance);

using var service = new QuestCatalogService(client, new Nulle(), seed);

var debut = DateTimeOffset.UtcNow;
var catalogue = await service.GetAsync(cancellationToken: CancellationToken.None);

Console.WriteLine($"lu en {(DateTimeOffset.UtcNow - debut).TotalSeconds:F0} s");

var succes = catalogue.Quests
    .Where(q => q.SuccessName.Length > 0)
    .Select(q => q.SuccessName)
    .ToHashSet(StringComparer.Ordinal);

var rangs = catalogue.SuccessOrder;
var reels = rangs.Where(succes.Contains).ToList();

Console.WriteLine($"quêtes                       : {catalogue.Quests.Count}");
Console.WriteLine($"succès portés par des quêtes : {succes.Count}");
Console.WriteLine($"entrées dans l'ordre         : {rangs.Count}");
Console.WriteLine($"dont rangs réels             : {reels.Count}");
Console.WriteLine($"fantômes                     : {rangs.Count - reels.Count}");
Console.WriteLine($"succès sans rang             : {succes.Count - reels.Count}");

foreach (var nom in succes.Except(reels, StringComparer.Ordinal).Order(StringComparer.CurrentCulture))
{
    Console.WriteLine($"   sans rang : {nom}");
}

// Ce que la liste d'une zone range encore par ordre alphabétique : un succès
// sans rang que nul prérequis ne relie, ni en amont ni en aval.
var sansRang = 0;
var alphabetiques = 0;

foreach (var zone in catalogue.Quests.GroupBy(q => q.SectionId))
{
    var quetes = zone.ToList();
    var index = new QuestChainIndex(quetes);

    foreach (var groupe in quetes
        .Where(q => q.SuccessName.Length > 0)
        .GroupBy(q => q.SuccessName, StringComparer.Ordinal))
    {
        if (reels.Contains(groupe.Key, StringComparer.Ordinal))
        {
            continue;
        }

        sansRang++;

        if (groupe.All(q => index.PreviousOf(q) is null && index.NextOf(q) is null))
        {
            alphabetiques++;
        }
    }
}

Console.WriteLine($"blocs de succès sans rang dans une liste de zone   : {sansRang}");
Console.WriteLine($"dont rangés par ordre alphabétique, faute de lien : {alphabetiques}");

internal sealed class Nulle : IDocumentStore<QuestCatalogDocument>
{
    public string FilePath => "(mémoire)";

    public Task<QuestCatalogDocument> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new QuestCatalogDocument());

    public Task SaveAsync(QuestCatalogDocument document, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public string Serialize(QuestCatalogDocument document) => string.Empty;

    public QuestCatalogDocument? Deserialize(string json) => null;
}
