using System.Globalization;
using System.Runtime.InteropServices;
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
    private string? _report;
    private bool _reportMode;

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

        // La fenêtre de signalement n'a qu'un formulaire à montrer : sa largeur
        // est celle que le pont donne au formulaire, et l'élargir ne montrerait
        // rien de plus. La hauteur, elle, reste libre, un petit écran ne
        // pouvant pas toujours le loger en entier.
        if (_reportMode)
        {
            MinWidth = Width;
            MaxWidth = Width;

            // WPF ne sait pas retirer le seul agrandissement : « CanMinimize »
            // fige aussi la hauteur. On ôte donc le style à la main, comme le
            // projet le fait déjà pour arrimer une fenêtre de jeu.
            _ = SetWindowLong(handle, GwlStyle, GetWindowLong(handle, GwlStyle) & ~WsMaximizeBox);
        }

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
    public Task ShowPageAsync(string url, string? title) => ShowAsync(url, title, report: null);

    /// <summary>
    /// Ouvre la page sur son formulaire de signalement, déplié et prêt à
    /// remplir, avec le repère d'étape déjà posé.
    ///
    /// Rien n'est envoyé : la fenêtre montre le formulaire du site, et c'est le
    /// lecteur qui écrit et qui décide d'appuyer.
    /// </summary>
    public Task ShowReportAsync(string url, string? title, string location) =>
        ShowAsync(url, title, location ?? string.Empty);

    private async Task ShowAsync(string url, string? title, string? report)
    {
        if (!PapychaSite.Owns(url))
        {
            _dialogs.OpenUrl(url);

            return;
        }

        _report = report;
        _reportMode = report is not null;

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
        //
        // Sauf en signalement : le cadrage masque le pied d'article, où vit le
        // formulaire. Celui-ci a son propre script, et il attend que la page
        // soit là, le formulaire n'existant pas avant.
        if (_report is null)
        {
            await View.CoreWebView2
                .AddScriptToExecuteOnDocumentCreatedAsync(QuestBridge.Script(framingOnly: true))
                .ConfigureAwait(true);
        }
        else
        {
            View.CoreWebView2.NavigationCompleted += OnReportPageLoaded;
        }

        if (!_watched)
        {
            _watched = true;

            View.CoreWebView2.NavigationStarting += OnNavigationStarting;
            View.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        }

        View.CoreWebView2.Navigate(url);
    }

    /// <summary>
    /// Prépare le formulaire, une fois et une seule.
    ///
    /// Une seule fois parce que l'envoi renvoie sur l'article : le rejouer
    /// masquerait la réponse du site, qui est justement ce qu'on veut lire
    /// après avoir appuyé.
    /// </summary>
    private async void OnReportPageLoaded(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        View.CoreWebView2.NavigationCompleted -= OnReportPageLoaded;

        if (!e.IsSuccess)
        {
            return;
        }

        try
        {
            var etat = await View.CoreWebView2
                .ExecuteScriptAsync(QuestBridge.ReportScript(_report))
                .ConfigureAwait(true);

            Log.Information("Formulaire de signalement : {Etat}.", etat);

            // « absent » : la page n'a pas de formulaire, ou le site a changé
            // son pied d'article. Le bouton ne paraît que sur un guide, où le
            // formulaire est toujours là ; on n'y arrive donc que si le site a
            // bougé. La fenêtre montre alors la page entière, et reprend un
            // titre qui ne promet plus ce qu'elle ne montre pas.
            if (etat.Contains("absent", StringComparison.Ordinal))
            {
                Title = "Guides de papycha.fr";
                return;
            }

            // Le script rend la hauteur qu'il faudrait pour montrer le
            // formulaire en entier. La fenêtre s'y pose, sans dépasser l'écran.
            if (Hauteur(etat) is { } voulue)
            {
                Height = Math.Min(voulue + (ActualHeight - View.ActualHeight), SystemParameters.WorkArea.Height);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "Le formulaire de signalement n'a pas pu être préparé.");
        }
    }

    /// <summary>
    /// La hauteur que le script annonce, ou <c>null</c> s'il n'en donne pas.
    ///
    /// Le moteur rend le résultat en JSON : « "pret 812" », guillemets compris.
    /// </summary>
    private static double? Hauteur(string etat)
    {
        var morceaux = etat.Trim('"', ' ').Split(' ');

        return morceaux.Length > 1
            && double.TryParse(morceaux[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var valeur)
            && valeur > 0
            ? valeur
            : null;
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

    // Le seul style qu'on retire : le bouton d'agrandissement de la fenêtre de
    // signalement.
    private const int GwlStyle = -16;

    private const int WsMaximizeBox = 0x00010000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
}
