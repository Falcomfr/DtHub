using System.Net;

namespace DtHub.Tests.Fakes;

/// <summary>Sert une réponse fixe et compte les requêtes réellement émises.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly byte[] _content;
    private readonly HttpStatusCode _statusCode;

    public FakeHttpMessageHandler(byte[] content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _content = content;
        _statusCode = statusCode;
    }

    public int RequestCount { get; private set; }

    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        RequestCount++;
        LastRequestUri = request.RequestUri;

        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new ByteArrayContent(_content),
        });
    }
}
