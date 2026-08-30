// Sonde de développement : interroge le vrai site une fois pour vérifier que
// le client rend ce qu'on attend. Jamais employée par l'application.
using DtHub.Core.Papycha;
using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new PapychaClient(http, NullLogger<PapychaClient>.Instance);

var progress = new Progress<QuestIndexingProgress>(
    p => Console.WriteLine($"  {p.Loaded}/{p.Total}"));

var chrono = System.Diagnostics.Stopwatch.StartNew();
var quests = await client.GetQuestsAsync(progress);
var sections = await client.GetSectionsAsync();
chrono.Stop();

Console.WriteLine($"\n{quests.Count} quetes, {sections.Count} rubriques en {chrono.Elapsed.TotalSeconds:N1} s");
Console.WriteLine($"niveaux renseignes : {quests.Count(q => q.Level > 0)}");
Console.WriteLine($"titres vides       : {quests.Count(q => string.IsNullOrWhiteSpace(q.Title))}");
Console.WriteLine($"entites restantes  : {quests.Count(q => q.Title.Contains("&#", StringComparison.Ordinal))}");

Console.WriteLine("\nrecherche « dragon astrub » :");
foreach (var q in QuestSearch.Filter(quests, "dragon astrub", 5))
{
    Console.WriteLine($"  {q.Title}  ->  {q.Url}");
}

Console.WriteLine("\nrecherche « completement givre » (sans accent) :");
foreach (var q in QuestSearch.Filter(quests, "completement givre", 5))
{
    Console.WriteLine($"  {q.Title}");
}
