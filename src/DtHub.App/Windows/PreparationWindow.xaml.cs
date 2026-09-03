using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.App.Services;
using DtHub.Core.Dependencies;
using DtHub.Core.Localization;

namespace DtHub.App.Windows;

/// <summary>
/// Le premier lancement, quand il y a quelque chose à mettre en place.
///
/// Dix-neuf mégaoctets se téléchargeaient jusqu'ici avant la première fenêtre,
/// sans rien à l'écran et avec un délai réseau de dix minutes : sur une ligne
/// lente, l'exécutable semblait mort. La fenêtre nomme ce qui manque, d'où ça
/// vient, et montre l'avancement que le provisionneur savait déjà rapporter.
///
/// Elle ne demande rien : le téléchargement est ce que l'application est venue
/// faire, et une question dont la seule réponse utile est « oui » n'est pas un
/// consentement. Elle informe, et se ferme d'elle-même.
/// </summary>
public partial class PreparationWindow : Window
{
    private readonly ToolPreparation _preparation;
    private readonly IReadOnlyList<ExternalDependency> _missing;
    private readonly List<ToolRow> _rows = [];
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _next;

    public PreparationWindow(ToolPreparation preparation, IReadOnlyList<ExternalDependency> missing)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(missing);

        InitializeComponent();

        _preparation = preparation;
        _missing = missing;

        foreach (var dependency in missing)
        {
            _rows.Add(new ToolRow
            {
                Name = $"{dependency.DisplayName} {dependency.Version}",
                Host = dependency.Url.Host,
                Status = Strings.Get("PreparationWaiting"),
            });
        }

        Rows.ItemsSource = _rows;
    }

    /// <summary>Achevé quand tout est posé, ou quand la personne renonce.</summary>
    public Task Completed => _completed.Task;

    /// <summary>
    /// Met en place ce qui manque, s'il manque quelque chose. Rend la main sans
    /// rien afficher quand les deux outils sont déjà là, c'est-à-dire à tous
    /// les lancements sauf le premier.
    /// </summary>
    public static async Task RunAsync(ToolPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        var missing = preparation.Missing();
        if (missing.Count == 0)
        {
            return;
        }

        var window = new PreparationWindow(preparation, missing);
        window.Show();

        try
        {
            await window.Completed.ConfigureAwait(true);
        }
        finally
        {
            window.Close();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _ = WorkAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Fermer la fenêtre à la croix vaut « continuer » : l'application sait
        // vivre sans ces outils, et rien ne doit rester en attente.
        _completed.TrySetResult();
        base.OnClosed(e);
    }

    private async Task WorkAsync()
    {
        while (_next < _missing.Count)
        {
            var dependency = _missing[_next];
            var row = _rows[_next];

            row.IsBusy = true;
            row.Status = Strings.Get("PreparationDownloading");

            // Créé sur le fil d'interface : Progress<T> retient son contexte de
            // synchronisation, les rapports du téléchargement reviennent donc
            // ici sans que rien n'ait à basculer de fil.
            var progress = new Progress<ProvisioningProgress>(step => Show(row, dependency, step));

            try
            {
                await _preparation.InstallAsync(dependency, progress).ConfigureAwait(true);
            }
            catch (DependencyProvisioningException exception)
            {
                Report(row, exception.Message);
                return;
            }

            row.IsBusy = false;
            row.Fraction = 1;
            row.Status = Strings.Get("PreparationDone");
            _next++;
        }

        _completed.TrySetResult();
    }

    private static void Show(ToolRow row, ExternalDependency dependency, ProvisioningProgress step)
    {
        row.Status = step.Stage switch
        {
            ProvisioningStage.Downloading => Weight(step, dependency),
            ProvisioningStage.Verifying => Strings.Get("PreparationVerifying"),
            ProvisioningStage.Extracting => Strings.Get("PreparationExtracting"),
            _ => Strings.Get("PreparationDone"),
        };

        // La barre ne s'anime que tant qu'on ignore où on en est : dès que la
        // taille est connue, elle avance pour de bon.
        if (step.Stage == ProvisioningStage.Downloading && step.Fraction is { } fraction)
        {
            row.Fraction = fraction;
            row.IsBusy = false;
        }
        else
        {
            row.IsBusy = true;
        }
    }

    /// <summary>« 7,2 / 11,3 Mo », dans les formats de la région.</summary>
    private static string Weight(ProvisioningProgress step, ExternalDependency dependency)
    {
        const double Megabyte = 1024 * 1024;

        // La taille attendue est déclarée dans le manifeste : elle sert de
        // repli quand le serveur ne l'annonce pas dans sa réponse.
        var total = step.TotalBytes ?? dependency.SizeBytes;

        return Strings.Format(
            "PreparationBytes",
            step.BytesReceived / Megabyte,
            total / Megabyte);
    }

    private void Report(ToolRow row, string message)
    {
        row.IsBusy = false;
        row.Fraction = 0;
        row.Status = Strings.Get("PreparationFailed");

        Footer.Text = message;
        Choices.Visibility = Visibility.Visible;
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        Choices.Visibility = Visibility.Collapsed;
        Footer.Text = Strings.Get("PreparationChecksum");

        _ = WorkAsync();
    }

    private void OnSkip(object sender, RoutedEventArgs e) => _completed.TrySetResult();

    /// <summary>Un composant et son avancement, tels que la fenêtre les montre.</summary>
    private sealed partial class ToolRow : ObservableObject
    {
        public required string Name { get; init; }

        public required string Host { get; init; }

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private double _fraction;

        [ObservableProperty]
        private bool _isBusy;
    }
}
