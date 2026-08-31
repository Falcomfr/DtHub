using System.Windows;

using DtHub.App.Services;

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

    private string _url = string.Empty;

    public QuestPageWindow(IDialogService dialogs)
    {
        _dialogs = dialogs;

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        lock (Handles)
        {
            Handles.Add(handle);
        }
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

        await View.EnsureCoreWebView2Async().ConfigureAwait(true);

        View.CoreWebView2.Navigate(url);
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) => _dialogs.OpenUrl(_url);
}
