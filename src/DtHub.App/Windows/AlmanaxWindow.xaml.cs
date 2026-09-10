using System.Globalization;
using System.Windows;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Almanax;
using DtHub.Core.Settings;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// L'Almanax du jour, lu sur le portail d'Ankama et redessiné ici.
///
/// La page n'est pas affichée : c'est une page de bureau entière, avec son
/// décor, ses encarts et ses textes d'ambiance, dont rien n'aide à savoir quoi
/// apporter aujourd'hui. Le moteur la charge, le pont en tire les quatre
/// champs utiles, et la fenêtre les dessine à sa façon.
///
/// Dans une fenêtre à part et non dans celle des guides : celle-ci tient une
/// recherche, un bandeau de quête et une chaîne de succès, et rien de tout
/// cela ne vaut pour l'Almanax, qui n'est pas une quête du catalogue et ne
/// vient pas du même site.
///
/// <see cref="AlmanaxCalendar" /> dit pourquoi c'est la seule source juste.
/// </summary>
public partial class AlmanaxWindow : Window
{
    private readonly IDialogService _dialogs;
    private readonly WindowPlacements _placements;
    private readonly SettingsService _settings;
    private readonly WebViewEnvironment _engine;
    private readonly AlmanaxViewModel _model = new();

    /// <summary>
    /// Les journées déjà lues, le temps de la session.
    ///
    /// Le calendrier est fixe : une journée lue ne changera plus. Revenir sur
    /// un jour déjà vu se fait donc sans recharger la page, ce qui rend la
    /// bande utilisable au lieu d'imposer une seconde d'attente par jour.
    ///
    /// En mémoire et non sur le disque : rien de la page n'est conservé après
    /// la session.
    /// </summary>
    private readonly Dictionary<DateOnly, AlmanaxDay> _read = [];

    private DateOnly _wanted = DateOnly.FromDateTime(DateTime.Now);
    private string _url = string.Empty;
    /// <summary>
    /// Le gréage, une fois et une seule.
    ///
    /// Un simple drapeau posé à la fin ne suffisait pas : il y a deux attentes
    /// avant lui, et deux clics rapprochés entraient tous deux avant que le
    /// premier ne l'ait posé. Le moteur recevait alors deux fois son pont et
    /// deux fois ses abonnements, et postait chaque lecture en double. On
    /// retient donc la tâche elle-même, que le second clic attend.
    /// </summary>
    private Task? _preparing;

    public AlmanaxWindow(
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

        DataContext = _model;

        _model.DateRequested += (_, date) => _ = LoadAsync(date);
    }

    /// <summary>
    /// Poignées des fenêtres ouvertes, pour que les raccourcis restent vivants
    /// quand l'une d'elles a le focus. Même raison que pour les pages liées :
    /// la question vient du guet du premier plan, qui ne vit pas sur le fil de
    /// l'interface et ne peut donc pas toucher une fenêtre WPF.
    /// </summary>
    private static readonly HashSet<nint> Handles = [];

    /// <summary>Vrai si cette poignée est celle d'un Almanax ouvert.</summary>
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

        try
        {
            var document = await _settings.GetAsync().ConfigureAwait(true);

            _placements.Restore(this, WindowPlacements.Almanax, document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La place de l'Almanax n'a pas pu être rétablie.");
        }
    }

    /// <summary>
    /// La place se retient ici et non dans <c>OnClosed</c> : la poignée n'existe
    /// déjà plus à ce moment-là, et il n'y aurait plus rien à interroger.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _ = _placements.SaveAsync(this, WindowPlacements.Almanax);

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

    /// <summary>Ouvre la fenêtre sur l'Almanax du jour.</summary>
    public async Task ShowAlmanaxAsync()
    {
        Show();
        Activate();

        await LoadAsync(DateOnly.FromDateTime(DateTime.Now)).ConfigureAwait(true);
    }

    /// <summary>
    /// Charge une journée, du cache si on l'a déjà lue, de la page sinon.
    /// </summary>
    private async Task LoadAsync(DateOnly date)
    {
        if (_model.AlreadyShowing(date))
        {
            return;
        }

        _wanted = date;

        if (_read.TryGetValue(date, out var known))
        {
            _model.Show(known);

            return;
        }

        // « BeginLoading » et non « Go » : « Go » lève la demande que cette
        // méthode écoute, et les deux se relanceraient l'un l'autre.
        _model.BeginLoading(date);

        try
        {
            await PrepareAsync().ConfigureAwait(true);

            _url = AlmanaxCalendar.UrlFor(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, date);

            View.CoreWebView2.Navigate(_url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "L'Almanax n'a pas pu être chargé.");

            // La tâche de gréage est oubliée : gardée en l'état, elle reste en
            // échec et toute nouvelle tentative échouerait sans même essayer.
            _preparing = null;

            _model.Fail();
        }
    }

    /// <summary>
    /// Le moteur, prêt et gréé, une seule fois.
    ///
    /// Le pont s'injecte à la création du document : posé après coup, il
    /// arriverait sur une page déjà bâtie et n'aurait rien à quoi s'accrocher
    /// pour la suivante.
    /// </summary>
    private Task PrepareAsync() => _preparing ??= SetUpEngineAsync();

    private async Task SetUpEngineAsync()
    {
        // Le même environnement que les autres fenêtres à page : un seul
        // profil, un seul cache, au même endroit.
        await View
            .EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        await View.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(AlmanaxBridge.Script())
            .ConfigureAwait(true);

        View.CoreWebView2.WebMessageReceived += OnBridgeMessage;
        View.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        View.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
    }

    /// <summary>
    /// Ce que le pont a lu de la page.
    ///
    /// Le message est refusé net si le bloc n'est pas celui de DOFUS Touch :
    /// le portail sert les deux jeux sur la même page, et l'Almanax de DOFUS
    /// demande d'autres objets. Mieux vaut dire qu'on n'a pas pu lire que
    /// donner l'offrande d'un autre jeu.
    /// </summary>
    private void OnBridgeMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string payload;

        try
        {
            payload = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // Le message n'est pas du texte. Le pont n'en poste jamais
            // d'autre, mais toute page peut appeler « postMessage ».
            return;
        }

        if (AlmanaxMessage.From(payload, _wanted) is not { } day)
        {
            Log.Warning("L'Almanax lu n'est pas celui de DOFUS Touch, ou la page a changé.");

            _model.Fail();

            return;
        }

        _read[day.Date] = day;

        // Seulement si c'est encore le jour demandé : on a pu cliquer ailleurs
        // pendant le chargement, et la page qui finit n'est plus celle qu'on
        // attend.
        if (day.Date == _wanted)
        {
            _model.Show(day);
        }
    }

    /// <summary>
    /// Le pont ne postera rien si la page n'est pas arrivée : c'est ici qu'on
    /// le sait, et pas ailleurs.
    /// </summary>
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            Log.Warning("L'Almanax n'a pas pu être joint : {Etat}.", e.WebErrorStatus);

            _model.Fail();
        }
    }

    /// <summary>
    /// « target="_blank" », que le moteur ouvrirait sinon dans une fenêtre à
    /// lui, hors de tout contrôle et sans rien qui la rattache à nous. La page
    /// n'est pas affichée, donc personne ne peut cliquer, mais elle garde son
    /// propre script et rien n'oblige à lui faire confiance.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        _dialogs.OpenUrl(e.Uri);
    }

    /// <summary>La page entière, pour qui veut le décor et les textes d'ambiance.</summary>
    private void OnOpenInBrowser(object sender, RoutedEventArgs e) =>
        _dialogs.OpenUrl(_url.Length > 0
            ? _url
            : AlmanaxCalendar.UrlFor(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, _wanted));
}
