using System.IO;

using DtHub.Core.Localization;
using DtHub.Core.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace DtHub.App.Services;

/// <summary>
/// The rendering engine, prepared once for every window that
/// carries one.
///
/// It serves two things the default setting handles poorly.
///
/// **Where it writes.** Without an address, it drops its cache next
/// to the executable. Measured on a fresh folder, twenty-four
/// megabytes after one session; on a development folder several
/// weeks old, three hundred and ninety-nine. A single file handed
/// to someone does not leave that behind: everything goes into the
/// user's folder, with the rest.
///
/// **How much it keeps.** Three hundred and thirty-eight of those
/// three hundred and ninety-nine megabytes are web cache, the
/// guides' images. Chromium sizes its cache on the disk's free
/// space, which is excessive here: we cap it.
/// </summary>
public sealed partial class WebViewEnvironment(IAppPaths paths, ILogger<WebViewEnvironment> logger)
{
    /// <summary>
    /// What we grant to the engine's cache. A hundred megabytes hold
    /// hundreds of guide pages, images included, and make a page
    /// already seen load instantly.
    /// </summary>
    private const long CacheBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Beyond this size, the cache is swept on startup.
    ///
    /// Safety net: the engine's documentation warns that some
    /// command-line switches are ignored, so the cap might not take
    /// effect. The threshold is set higher than it, so we only
    /// sweep if the cap has failed.
    /// </summary>
    private const long SweepBytes = 200L * 1024 * 1024;

    private readonly IAppPaths _paths = paths;
    private readonly ILogger<WebViewEnvironment> _logger = logger;

    private Task<CoreWebView2Environment>? _ready;

    /// <summary>
    /// The environment, created on first need then shared. Both
    /// windows thus write into the same profile instead of each
    /// opening one.
    /// </summary>
    public Task<CoreWebView2Environment> GetAsync() => _ready ??= CreateAsync();

    private async Task<CoreWebView2Environment> CreateAsync()
    {
        Available();

        _ = Directory.CreateDirectory(_paths.WebViewDirectory);

        Sweep();

        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = $"--disk-cache-size={CacheBytes}",
        };

        return await CoreWebView2Environment
            .CreateAsync(browserExecutableFolder: null, _paths.WebViewDirectory, options)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Checks that the component is present, and says so clearly if
    /// it is not.
    ///
    /// This is the application's only external dependency. It ships
    /// with Windows 11 and Windows 10 kept up to date; when missing,
    /// the guides window used to stay blank and the incident could
    /// only be read in a log.
    ///
    /// The check lives here rather than in the windows: both go
    /// through this environment, which makes it the only place to
    /// maintain. The message travels up through the exception, which
    /// the window already shows in place of the page.
    /// </summary>
    private void Available()
    {
        string? version;

        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // Silence is deliberate here: the engine's absence is
            // the answer, not a fault. It is the caller that turns
            // it into a message.
            version = null;
        }

        if (!string.IsNullOrEmpty(version))
        {
            return;
        }

        LogMissing();

        throw new WebView2RuntimeNotFoundException(
            Strings.Format(
                "WebViewMissingFull",
                "https://developer.microsoft.com/microsoft-edge/webview2/"));
    }

    /// <summary>
    /// Clears the cache if it has overflowed, before the engine
    /// starts. It rebuilds it without complaint; all that is lost
    /// is one load.
    /// </summary>
    private void Sweep()
    {
        var cache = Path.Combine(_paths.WebViewDirectory, "EBWebView", "Default", "Cache");

        try
        {
            if (!Directory.Exists(cache))
            {
                return;
            }

            var size = new DirectoryInfo(cache)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);

            if (size < SweepBytes)
            {
                return;
            }

            Directory.Delete(cache, recursive: true);
            LogSwept(size / (1024 * 1024));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cache we fail to clear does not deserve to stop the
            // application from starting: it will be picked up again
            // at the next launch.
            LogSweepFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Le composant WebView2 est absent : les guides ne peuvent pas s'afficher.")]
    private partial void LogMissing();

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache du moteur de rendu balayé : {megabytes} Mo.")]
    private partial void LogSwept(long megabytes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le cache du moteur de rendu n'a pas pu être balayé.")]
    private partial void LogSweepFailed(Exception exception);
}
