using System.Windows;

using DtHub.App.Services;
using DtHub.Core.Localization;

namespace DtHub.App.Windows;

/// <summary>
/// Ce qu'on montre quand quelque chose a échoué.
///
/// L'ancienne boîte affichait une phrase et le chemin du dossier de journaux,
/// avec un seul bouton « OK ». Le message de l'exception n'atteignait jamais
/// l'écran, le chemin ne se cliquait pas, et il n'y avait rien à envoyer à
/// personne.
///
/// Celle-ci montre la faute, laisse lire le rapport avant de le copier, et
/// ouvre le formulaire de signalement du dépôt. **Rien ne part d'ici** : le
/// rapport va dans le presse-papiers, et c'est la personne qui décide. C'est la
/// ligne que suit déjà le signalement vers papycha.fr.
/// </summary>
public partial class ProblemWindow : Window
{
    private readonly IDialogService _dialogs;
    private readonly string _report;
    private readonly string _headline;

    public ProblemWindow(IDialogService dialogs, string headline, string report, string? detail)
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        InitializeComponent();

        _dialogs = dialogs;
        _report = report;
        _headline = headline;

        // Le titre suit ce qu'on montre : « un problème est survenu » mentirait
        // quand c'est la personne qui vient raconter quelque chose d'elle-même.
        Title = headline;
        Headline.Text = headline;
        Report.Text = report;

        Detail.Text = detail ?? string.Empty;
        Detail.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        _dialogs.CopyToClipboard(_report);
        Copied.Visibility = Visibility.Visible;
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) =>
        _dialogs.OpenFolder(Logs);

    private void OnReport(object sender, RoutedEventArgs e)
    {
        // Le rapport part au presse-papiers avant l'ouverture : le formulaire
        // demande de le coller, et l'avoir déjà sous la main évite un aller et
        // retour entre deux fenêtres.
        _dialogs.CopyToClipboard(_report);
        Copied.Visibility = Visibility.Visible;

        _dialogs.OpenUrl(DiagnosticReporter.IssueUrl(_headline));
    }

    /// <summary>Le dossier des journaux, posé par l'appelant.</summary>
    public required string Logs { get; init; }

    /// <summary>Le titre de la fenêtre, tel que les ressources le donnent.</summary>
    public static string DefaultHeadline => Strings.Get("UnexpectedError");
}
