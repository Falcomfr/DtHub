using System.ComponentModel;
using System.Windows;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core;
using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;

namespace DtHub.App;

/// <summary>
/// Fenêtre principale. Ne contient que ce que XAML ne sait pas exprimer :
/// l'amorçage, la boîte de saisie de nom, et la politique de fermeture.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly SessionOrchestrator _orchestrator;
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;

    private bool _closingHandled;

    public MainWindow(
        ShellViewModel shell,
        SessionOrchestrator orchestrator,
        SettingsService settings,
        IDialogService dialogs,
        DevicesViewModel devices,
        ProfilesViewModel profiles)
    {
        _shell = shell;
        _orchestrator = orchestrator;
        _settings = settings;
        _dialogs = dialogs;

        InitializeComponent();

        DataContext = shell;
        Title = ProductInfo.Name;

        // Les demandes de saisie remontent à la fenêtre : les vues-modèles
        // n'ouvrent jamais de fenêtre elles-mêmes.
        devices.RenameRequested = current => PromptForName("Nom de l'appareil", current);
        profiles.NameRequested = current => PromptForName("Nom du profil", current);

        Loaded += async (_, _) => await _shell.InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Demande un texte court. Une boîte modale minimale suffit et évite une
    /// fenêtre supplémentaire à maintenir.
    /// </summary>
    private string? PromptForName(string title, string current)
    {
        var dialog = new Views.TextPromptWindow(title, current) { Owner = this };

        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closingHandled)
        {
            base.OnClosing(e);
            return;
        }

        var sessions = _orchestrator.ActiveSessions;
        if (sessions.Count == 0)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        var settings = await _settings.GetAsync().ConfigureAwait(true);
        var close = settings.ExitPolicy switch
        {
            ExitPolicy.CloseSessions => true,
            ExitPolicy.KeepSessions => false,
            _ => AskExitPolicy(sessions),
        };

        if (close is null)
        {
            // L'utilisateur a renoncé à fermer.
            return;
        }

        if (close.Value)
        {
            await _orchestrator.CloseAllAsync().ConfigureAwait(true);
        }

        _closingHandled = true;
        Close();
    }

    private bool? AskExitPolicy(IReadOnlyList<ScrcpySession> sessions) =>
        _dialogs.ConfirmWithCancel(
            $"{sessions.Count} session(s) sont encore ouvertes.\n\n"
            + "Oui : les fermer en quittant.\n"
            + "Non : les laisser ouvertes.\n"
            + "Annuler : rester dans DT Hub.",
            "Quitter " + ProductInfo.Name);
}
