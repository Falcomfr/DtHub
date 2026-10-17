using System.Net;
using System.Text;

using DtHub.Core.Papycha;
using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Papycha;

public class PapychaClientTests
{
    /// <summary>
    /// Answers by path and query, relative to the API's root. An unknown
    /// address answers 404, as the site would.
    /// </summary>
    private sealed class Routes(Dictionary<string, string> answers) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri!.PathAndQuery.Replace("/wp-json/papycha-public/v1/", string.Empty, StringComparison.Ordinal);
            Asked.Add(key);

            return Task.FromResult(answers.TryGetValue(key, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static (PapychaClient Client, Routes Routes) Create(Dictionary<string, string> answers)
    {
        var routes = new Routes(answers);

        return (new PapychaClient(new HttpClient(routes), NullLogger<PapychaClient>.Instance), routes);
    }

    private const string Naufrages = """
        {"id":47974,"slug":"lile-des-naufrages","name":"L’île des naufragés","url":"https://papycha.fr/lile-des-naufrages/",
         "level":null,"zone":[{"id":1560,"slug":"sufokia","name":"Sufokia"}],"section":[{"id":724,"slug":"a-classer","name":"[A classer]"}],
         "start_coordinates":"[15,28]","start_npc":null,"access_conditions":["- Alignement bontarien 40 minimum."],
         "previous_quests":[{"id":3692,"slug":"quete-le-nouveau-monde","name":"Le nouveau monde","url":"https://papycha.fr/quete-le-nouveau-monde/"}],
         "next_quests":[]}
        """;

    private const string Chaines = """
        {"id":14099,"name":"A chacun ses chaînes.","url":"https://papycha.fr/quete-a-chacun-ses-chaines/","level":52,
         "zone":[],"section":[{"id":668,"name":"Île d'Orado"},{"id":7,"name":"[Quêtes]"}],
         "start_coordinates":null,"access_conditions":[],"previous_quests":[],"next_quests":[]}
        """;

    [Fact]
    public async Task Les_quetes_sont_lues_page_par_page_et_converties()
    {
        var (client, routes) = Create(new()
        {
            ["quests?per_page=100&page=1"] = $$"""{"items":[{{Naufrages}}],"page":1,"per_page":100,"total":2,"total_pages":2}""",
            ["quests?per_page=100&page=2"] = $$"""{"items":[{{Chaines}}],"page":2,"per_page":100,"total":2,"total_pages":2}""",
        });
        List<QuestIndexingProgress> seen = [];

        var quests = await client.GetQuestsAsync(new SynchronousProgress(seen.Add));

        Assert.Equal(2, quests.Count);
        var first = quests[0];
        Assert.Equal(47974, first.Id);
        Assert.Equal("L’île des naufragés", first.Title);
        Assert.Equal(QuestSearch.Normalize("L’île des naufragés"), first.SearchKey);
        Assert.Equal("https://papycha.fr/lile-des-naufrages/", first.Url);
        Assert.Equal(0, first.Level);
        Assert.Equal([724, 1560], first.Categories);
        Assert.Equal("[15,28]", first.StartPosition);
        Assert.Equal(["Alignement bontarien 40 minimum.", "Le nouveau monde"], first.Prerequisites);
        Assert.Equal(52, quests[1].Level);
        Assert.Equal([668, 7], quests[1].Categories);
        Assert.Equal(string.Empty, quests[1].StartPosition);
        Assert.Equal(["quests?per_page=100&page=1", "quests?per_page=100&page=2"], routes.Asked);
        Assert.Equal(new QuestIndexingProgress(2, 2, QuestIndexingPhase.Quests), seen[^1]);
    }

    [Fact]
    public async Task Un_nom_avec_des_entites_html_est_decode()
    {
        var (client, _) = Create(new()
        {
            ["quests?per_page=100&page=1"] = """{"items":[{"id":1,"name":"Bonta &amp; Cania, l&rsquo;union","url":"u","section":[],"zone":[]}],"total":1,"total_pages":1}""",
        });

        var quest = Assert.Single(await client.GetQuestsAsync());

        Assert.Equal("Bonta & Cania, l’union", quest.Title);
        Assert.Equal(QuestSearch.Normalize("Bonta & Cania, l’union"), quest.SearchKey);
    }

    [Fact]
    public async Task Une_reponse_sans_nombre_de_pages_s_arrete_a_la_premiere()
    {
        var (client, routes) = Create(new()
        {
            ["quests?per_page=100&page=1"] = """{"items":[{"id":1,"name":"Seule","url":"u"}]}""",
        });

        var quests = await client.GetQuestsAsync();

        Assert.Single(quests);
        Assert.Single(routes.Asked);
    }

    [Fact]
    public async Task Une_liste_refusee_est_une_panne()
    {
        var (client, _) = Create([]);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetQuestsAsync());
    }

    [Fact]
    public async Task Les_donjons_perdent_leur_prefixe_et_gardent_clef_et_pierre()
    {
        var (client, _) = Create(new()
        {
            ["dungeons?per_page=100&page=1"] = """
                {"items":[
                  {"id":1452,"name":"[Donjon] Abraknydes","url":"https://papycha.fr/donjon-des-abraknydes/","kind":"dungeon","level":90,
                   "entrance_coordinates":"[-9,-14]","contact_npc":"Tronkny Baussope","key":{"id":1175,"name":"Clef des Abraknydes"},"soul_stone_size":"Moyenne"},
                  {"id":9,"name":"[Tanière] Arakne","url":"https://papycha.fr/taniere-arakne/","kind":"den","level":null,"key":null},
                  {"id":8,"name":"[Raid] Belladone","url":"https://papycha.fr/raid/","kind":"raid","level":200}
                ],"total":3,"total_pages":1}
                """,
        });

        var dungeons = await client.GetDungeonsAsync();

        var abra = dungeons[0];
        Assert.Equal(DungeonKind.Dungeon, abra.Kind);
        Assert.Equal("Abraknydes", abra.Title);
        Assert.Equal(QuestSearch.Normalize("Abraknydes"), abra.SearchKey);
        Assert.Equal(90, abra.Level);
        Assert.Equal("[-9,-14]", abra.Position);
        Assert.Equal("Tronkny Baussope", abra.Person);
        Assert.Equal("Clef des Abraknydes", abra.Key);
        Assert.Equal("Moyenne", abra.SoulStone);
        Assert.Equal(DungeonKind.Lair, dungeons[1].Kind);
        Assert.Equal("Arakne", dungeons[1].Title);
        Assert.Equal(string.Empty, dungeons[1].Key);
        Assert.Equal(0, dungeons[1].Level);
        Assert.Equal(DungeonKind.Raid, dungeons[2].Kind);
    }

    [Fact]
    public async Task Les_chemins_perdent_leur_prefixe()
    {
        var (client, _) = Create(new()
        {
            ["paths?per_page=100&page=1"] = """{"items":[{"id":3711,"name":"[Chemin] Aller sur l’Île d’Otomaï","url":"https://papycha.fr/aller-sur-lile-dotomai/"}],"total":1,"total_pages":1}""",
        });

        var path = Assert.Single(await client.GetPathsAsync());

        Assert.Equal(3711, path.Id);
        Assert.Equal("Aller sur l’Île d’Otomaï", path.Title);
        Assert.Equal("https://papycha.fr/aller-sur-lile-dotomai/", path.Url);
    }

    [Fact]
    public async Task Le_tampon_vient_du_statut()
    {
        var (client, _) = Create(new()
        {
            ["status"] = """{"name":"Papycha Public Content API","version":"0.1.6","enabled":true,"last_updated":"2026-10-15T19:00:06+00:00"}""",
        });

        var stamp = await client.GetStampAsync();

        Assert.Equal(new DateTimeOffset(2026, 10, 15, 19, 0, 6, TimeSpan.Zero), stamp?.Modified);
    }

    [Fact]
    public async Task Un_statut_illisible_rend_null()
    {
        var (missing, _) = Create([]);
        var (garbled, _) = Create(new() { ["status"] = "<html>maintenance</html>" });

        Assert.Null(await missing.GetStampAsync());
        Assert.Null(await garbled.GetStampAsync());
    }

    private sealed class SynchronousProgress(Action<QuestIndexingProgress> report) : IProgress<QuestIndexingProgress>
    {
        public void Report(QuestIndexingProgress value) => report(value);
    }
}
