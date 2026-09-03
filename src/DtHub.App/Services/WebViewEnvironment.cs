using System.IO;

using DtHub.Core.Localization;
using DtHub.Core.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace DtHub.App.Services;

/// <summary>
/// Le moteur de rendu, préparé une fois pour toutes les fenêtres qui en portent
/// un.
///
/// Il sert deux choses que le réglage par défaut fait mal.
///
/// **Où il écrit.** Sans adresse, il pose son cache à côté de l'exécutable.
/// Mesuré sur un dossier vierge, vingt-quatre mégaoctets après une session ; sur
/// un dossier de développement de quelques semaines, trois cent
/// quatre-vingt-dix-neuf. Un fichier unique qu'on donne à quelqu'un ne laisse pas
/// cela derrière lui : tout va dans le dossier de l'utilisateur, avec le reste.
///
/// **Combien il garde.** Trois cent trente-huit de ces trois cent
/// quatre-vingt-dix-neuf mégaoctets sont du cache web, les images des guides.
/// Chromium dimensionne son cache sur la place libre du disque, ce qui est
/// démesuré ici : on le borne.
/// </summary>
public sealed partial class WebViewEnvironment(IAppPaths paths, ILogger<WebViewEnvironment> logger)
{
    /// <summary>
    /// Ce qu'on accorde au cache du moteur. Cent mégaoctets tiennent des
    /// centaines de pages de guide, images comprises, et rendent une page déjà
    /// vue immédiate.
    /// </summary>
    private const long CacheBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Au-delà de cette taille, le cache est balayé au démarrage.
    ///
    /// Filet : la documentation du moteur prévient que certains commutateurs de
    /// ligne de commande sont ignorés, et la borne pourrait donc ne pas prendre.
    /// Le seuil est plus haut qu'elle, pour ne balayer que si elle a échoué.
    /// </summary>
    private const long SweepBytes = 200L * 1024 * 1024;

    private readonly IAppPaths _paths = paths;
    private readonly ILogger<WebViewEnvironment> _logger = logger;

    private Task<CoreWebView2Environment>? _ready;

    /// <summary>
    /// L'environnement, créé au premier besoin puis partagé. Les deux fenêtres
    /// écrivent ainsi dans le même profil au lieu d'en ouvrir chacune un.
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
    /// Vérifie que le composant est là, et le dit clairement s'il ne l'est pas.
    ///
    /// C'est la seule dépendance externe de l'application. Il est fourni avec
    /// Windows 11 et les Windows 10 tenus à jour ; absent, la fenêtre des guides
    /// restait vide et l'incident ne se lisait que dans un journal.
    ///
    /// Le contrôle est ici et non dans les fenêtres : les deux passent par cet
    /// environnement, ce qui en fait le seul endroit à tenir. Le message remonte
    /// par l'exception, que la fenêtre affiche déjà à la place de la page.
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
            // Silence assumé : l'absence du moteur est la réponse, non une
            // faute. C'est l'appelant qui la transforme en message.
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
    /// Efface le cache s'il a débordé, avant que le moteur ne démarre. Il le
    /// rebâtit sans se plaindre ; on n'y perd qu'un chargement.
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
            // Un cache qu'on n'arrive pas à effacer ne mérite pas d'empêcher
            // l'application de démarrer : il sera repris au lancement suivant.
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
