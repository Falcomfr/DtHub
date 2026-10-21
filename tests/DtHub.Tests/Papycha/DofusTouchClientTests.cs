using System.Net;
using System.Text;

using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Papycha;

public class DofusTouchClientTests
{
    /// <summary>Answers one fixed status and body, and keeps what was asked.</summary>
    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Asked { get; private set; }

        public string? Sent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Asked = $"{request.Method} {request.RequestUri!.PathAndQuery}";
            Sent = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (DofusTouchClient Client, Answer Answer) Create(HttpStatusCode status, string body)
    {
        var answer = new Answer(status, body);

        return (new DofusTouchClient(new HttpClient(answer), NullLogger<DofusTouchClient>.Instance), answer);
    }

    [Fact]
    public async Task La_requete_poste_la_classe_et_les_ids_dans_la_langue()
    {
        var (client, answer) = Create(
            HttpStatusCode.OK,
            """{"230":{"id":230,"nameId":"Invented quest","levelMin":80,"levelMax":80,"stepIds":[421]}}""");

        var rows = await client.GetAsync("Quests", [230], "en");

        Assert.Equal("POST /data/map?lang=en", answer.Asked);
        Assert.Equal("""{"class":"Quests","ids":[230]}""", answer.Sent);
        Assert.Equal("Invented quest", rows[230].GetProperty("nameId").GetString());
    }

    [Fact]
    public async Task Sans_ids_la_requete_les_omet()
    {
        var (client, answer) = Create(HttpStatusCode.OK, "{}");

        await client.GetAsync("Quests", null, "fr");

        Assert.Equal("""{"class":"Quests"}""", answer.Sent);
    }

    [Fact]
    public async Task Les_textes_se_lisent_par_ids()
    {
        var (client, answer) = Create(HttpStatusCode.OK, """{"1":"Texto"}""");

        var texts = await client.GetTextsAsync([1], "es");

        Assert.Equal("POST /data/text?lang=es", answer.Asked);
        Assert.Equal("""{"ids":[1]}""", answer.Sent);
        Assert.Equal("Texto", texts[1]);
    }

    [Fact]
    public async Task Un_refus_403_leve_une_exception()
    {
        var (client, _) = Create(HttpStatusCode.Forbidden, "<html>403 Forbidden</html>");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("Quests", [1], "en"));
    }
}
