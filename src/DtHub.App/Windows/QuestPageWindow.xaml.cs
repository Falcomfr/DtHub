using System.Windows;

using Microsoft.Web.WebView2.Core;

using DtHub.App.Services;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;

using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// Une page ouverte depuis un lien d'un guide.
///
/// Sans elle, un clic dans le guide faisait naviguer la fenêtre de quêtes en
/// place : le bandeau gardait le titre de l'ancienne quête, les étapes
/// devenaient celles de la nouvelle page, et « Ouvrir dans le navigateur »
/// pointait ailleurs que ce qui était affiché. Une fenêtre à part n'a rien à
/// tenir à jour et ne ment donc sur rien.
/// </summary>
public partial class QuestPageWindow : Window
{
    private readonly IDialogService _dialogs;
    private readonly WindowPlacements _placements;
    private readonly SettingsService _settings;
    private readonly WebViewEnvironment _engine;

    private string _url = string.Empty;
    private bool _watched;

    public QuestPageWindow(
        IDialogService dialogs,
        WindowPlacements placements,
        SettingsService settings,
        WebViewEnvironment engine)
    {
        _dialogs = dialogs;
        _placements = placements;
        _settings = settings;
        _engine = engine;

        InitializeComponent();
    }

    /// <summary>
    /// Poignées des pages ouvertes, pour que les raccourcis restent vivants
    /// quand l'une d'elles a le focus.
    ///
    /// Un simple ensemble d'entiers, et non la liste des fenêtres de
    /// l'application : la question est posée depuis le guet du premier plan,
    /// qui ne vit pas sur le fil de l'interface. Y toucher une fenêtre WPF lève
    /// aussitôt, et le raccourci meurt sans que rien ne le dise.
    /// </summary>
    private static readonly HashSet<nint> Handles = [];

    /// <summary>Vrai si cette poignée est celle d'une page ouverte.</summary>
    public static bool Owns(nint handle)
    {
        lock (Handles)
        {
            return Handles.Contains(handle);
        }
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        lock (Handles)
        {
            Handles.Add(handle);
        }

        // Une seule place pour toutes ces fenêtres : elles se succèdent au fil
        // des liens et ne se distinguent pas les unes des autres. Ce qu'on veut
        // retrouver, c'est l'endroit où l'on a posé « la fenêtre des pages ».
        try
        {
            var document = await _settings.GetAsync().ConfigureAwait(true);

            _placements.Restore(this, WindowPlacements.LinkedPage, document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La place de la page liée n'a pas pu être rétablie.");
        }
    }

    /// <summary>
    /// La place se retient ici et non dans <c>OnClosed</c> : la poignée n'existe
    /// déjà plus à ce moment-là, et il n'y aurait plus rien à interroger.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _ = _placements.SaveAsync(this, WindowPlacements.LinkedPage);

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        lock (Handles)
        {
            Handles.Remove(handle);
        }

        View.Dispose();

        base.OnClosed(e);
    }

    /// <summary>
    /// Ouvre la page et se montre, si c'est bien une page du site.
    ///
    /// Cette fenêtre n'a pas de barre d'adresse : on y voit une page sans
    /// savoir d'où elle vient, sous notre titre et notre icône. Elle ne reçoit
    /// donc que le site, et le reste part au navigateur. Sans cette réserve,
    /// n'importe quel lien d'un guide ouvrait n'importe quelle adresse ici,
    /// « file:// » compris.
    /// </summary>
    public async Task ShowPageAsync(string url, string? title)
    {
        if (!PapychaSite.Owns(url))
        {
            _dialogs.OpenUrl(url);

            return;
        }

        _url = url;

        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }

        Show();
        Activate();

        // Le même environnement que la fenêtre des guides : un seul profil, un
        // seul cache, au même endroit.
        await View
            .EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        // Avant de naviguer : le script s'injecte à la création du document, et
        // une page déjà chargée ne le verrait pas passer. Cadrage seul, sans le
        // suivi d'étapes qui ne vaut que pour un guide.
        await View.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(QuestBridge.Script(framingOnly: true))
            .ConfigureAwait(true);

        if (!_watched)
        {
            _watched = true;

            View.CoreWebView2.NavigationStarting += OnNavigationStarting;
            View.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        }

        View.CoreWebView2.Navigate(url);
    }

    /// <summary>
    /// Retient ici la navigation, et confie au navigateur ce qui sort du site.
    ///
    /// La fenêtre suivait jusqu'ici tout ce qu'une page lui demandait de
    /// suivre. Une page du site qui renvoie ailleurs, ou qu'on aurait
    /// remplacée, emmenait donc la fenêtre où elle voulait, sans que le bouton
    /// « ouvrir dans le navigateur » cesse pour autant de désigner la page
    /// d'origine.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (PapychaSite.Owns(e.Uri))
        {
            _url = e.Uri;

            return;
        }

        e.Cancel = true;

        _dialogs.OpenUrl(e.Uri);
    }

    /// <summary>
    /// Et « target="_blank" », que le moteur ouvrirait sinon dans une fenêtre
    /// à lui, hors de tout contrôle et sans rien qui la rattache à nous.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        _dialogs.OpenUrl(e.Uri);
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) => _dialogs.OpenUrl(_url);
}
