using System.Windows;

using DtHub.App.Services;
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

    /// <summary>Ouvre la page et se montre.</summary>
    public async Task ShowPageAsync(string url, string? title)
    {
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

        View.CoreWebView2.Navigate(url);
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) => _dialogs.OpenUrl(_url);
}
