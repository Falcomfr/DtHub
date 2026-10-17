// Development probe. Never used by the application.
//
// It queries papycha's public API and checks not fixed numbers, but the
// assumptions the catalog merge depends on: the API answers, it still
// serves the quests, dungeons and paths the embedded catalog knows by
// their id, and it fills the fields the merge takes from it.
//
// It returns zero if everything holds, one otherwise. The reference
// reading is reference.json, versioned alongside; it is re-blessed by
// hand with --benir when a discrepancy is legitimate.
//
// The checks on the shape of the guide pages are gone: they read every
// article body through the WordPress API, which the site closed on
// 2026-10-12.
//
// To run before shipping: dotnet run --project build/sonde-papycha
using System.Text.Json;

using DtHub.Core.Papycha;
using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

var benir = args.Contains("--benir", StringComparer.Ordinal);
var reference = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "reference.json");

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("DtHub-sonde/1.0");

var client = new PapychaClient(http, NullLogger<PapychaClient>.Instance);
var embarque = new EmbeddedQuestCatalogSnapshot(NullLogger<EmbeddedQuestCatalogSnapshot>.Instance).Load();
var constats = new List<Constat>();
var mesures = new Dictionary<string, int>(StringComparer.Ordinal);

Console.WriteLine("Lecture de l'API publique…\n");

// ---------------------------------------------------------------------------
// What the client returns, through the path the application uses.
// ---------------------------------------------------------------------------
var tampon = await client.GetStampAsync();
var quetes = await client.GetQuestsAsync();
var lieux = await client.GetDungeonsAsync();
var chemins = await client.GetPathsAsync();

Exige("l'API annonce sa date de mise à jour", tampon is null ? 0 : 1, 1);
Exige("l'API sert des quêtes", quetes.Count > 0 ? 1 : 0, 1);

mesures["quetes"] = quetes.Count;
mesures["donjons"] = lieux.Count(d => d.Kind == DungeonKind.Dungeon);
mesures["raids"] = lieux.Count(d => d.Kind == DungeonKind.Raid);
mesures["tanieres"] = lieux.Count(d => d.Kind == DungeonKind.Lair);
mesures["chemins"] = chemins.Count;
mesures["quetes-avec-niveau"] = quetes.Count(q => q.Level > 0);
mesures["quetes-avec-depart"] = quetes.Count(q => q.StartPosition.Length > 0);
mesures["quetes-avec-prerequis"] = quetes.Count(q => q.Prerequisites.Count > 0);
mesures["lieux-avec-niveau"] = lieux.Count(d => d.Level > 0);

// The merge matches by id. The day the site renumbers its articles, every
// quest would arrive twice: once kept from the embedded catalog, once added
// from the API. This is the number that would say so.
var servies = quetes.Select(q => q.Id).ToHashSet();
mesures["quetes-du-catalogue-embarque-encore-servies"] = embarque.Quests.Count(q => servies.Contains(q.Id));

var lieuxServis = lieux.Select(d => d.Id).ToHashSet();
mesures["lieux-du-catalogue-embarque-encore-servis"] = embarque.Dungeons.Count(d => lieuxServis.Contains(d.Id));

var cheminsServis = chemins.Select(p => p.Id).ToHashSet();
mesures["chemins-du-catalogue-embarque-encore-servis"] = embarque.Paths.Count(p => cheminsServis.Contains(p.Id));

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
void Exige(string quoi, int obtenu, int attendu)
{
    constats.Add(new Constat(obtenu >= attendu, $"{quoi} : {obtenu} / {attendu}"));
}

internal sealed record Constat(bool Bon, string Texte);
