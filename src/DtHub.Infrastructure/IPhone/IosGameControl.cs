using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using DtHub.Core.IPhone;
using DtHub.Core.Localization;

namespace DtHub.Infrastructure.IPhone;

/// <summary>
/// DOFUS Touch on an iPhone whose screen is shown, through the commands
/// <see cref="PyMobileDevice.MirrorBootstrap"/> serves inside the stream's
/// own process, on the control port given to <see cref="PyMobileDevice.StartMirrorAsync"/>.
/// </summary>
public sealed class IosGameControl(int controlPort)
{
    // Listing the apps or launching one goes over the device's tunnel: seconds, not minutes.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Uri _root = new($"http://127.0.0.1:{controlPort.ToString(CultureInfo.InvariantCulture)}/");

    /// <summary>The game's bundle id, <c>null</c> when it is not installed.</summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task<string?> FindDofusAsync(CancellationToken cancellationToken) =>
        Read(await SendAsync(HttpMethod.Get, "apps", bundle: null, cancellationToken).ConfigureAwait(false), IosApps.FindDofus);

    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task<bool> IsRunningAsync(string bundle, CancellationToken cancellationToken) =>
        Read(
            await SendAsync(HttpMethod.Get, "running?bundle=" + Uri.EscapeDataString(bundle), bundle: null, cancellationToken).ConfigureAwait(false),
            json =>
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.GetProperty("pid").ValueKind == JsonValueKind.Number;
            });

    /// <summary>Starts the game, or brings it forward when it already runs.</summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public Task LaunchAsync(string bundle, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, "launch", bundle, cancellationToken);

    /// <summary>Ends the game; does nothing when it does not run.</summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public Task StopAsync(string bundle, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, "stop", bundle, cancellationToken);

    private async Task<string> SendAsync(HttpMethod method, string path, string? bundle, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_root, path))
        {
            Content = bundle is null ? null : JsonContent.Create(new Dictionary<string, string> { ["bundle"] = bundle }),
        };

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // The device's own error stays in the inner exception, for the log.
            return response.IsSuccessStatusCode
                ? text
                : throw new IPhoneException(Strings.Get("IPhoneGameFailed"), new HttpRequestException(text, null, response.StatusCode));
        }
        catch (Exception exception) when (exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new IPhoneException(Strings.Get("IPhoneGameFailed"), exception);
        }
    }

    private static T Read<T>(string json, Func<string, T> parse)
    {
        try
        {
            return parse(json);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new IPhoneException(Strings.Get("IPhoneGameFailed"), exception);
        }
    }
}
