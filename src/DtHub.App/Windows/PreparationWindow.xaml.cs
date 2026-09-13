using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.App.Services;
using DtHub.Core.Dependencies;
using DtHub.Core.Localization;

namespace DtHub.App.Windows;

/// <summary>
/// The first launch, when there is something to set up.
///
/// Until now, nineteen megabytes used to download before the first
/// window, with nothing on screen and a network timeout of ten
/// minutes: on a slow line, the executable seemed dead. The window
/// names what is missing, where it comes from, and shows the
/// progress that the provisioner already knew how to report.
///
/// It asks nothing: the download is what the application came to
/// do, and a question whose only useful answer is "yes" is not
/// consent. It informs, and closes itself.
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

    /// <summary>
    /// Completed when everything is set up, or when the person gives
    /// up.
    /// </summary>
    public Task Completed => _completed.Task;

    /// <summary>
    /// Sets up what is missing, if anything is missing. Returns
    /// control without showing anything when both tools are already
    /// there, that is to say on every launch except the first.
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
        // Closing the window via the X counts as "continue": the
        // application knows how to live without these tools, and
        // nothing must be left waiting.
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

            // Created on the UI thread: Progress<T> keeps its
            // synchronization context, so the download reports come
            // back here without anything having to switch threads.
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

        // The bar only animates as long as we do not know where
        // things stand: as soon as the size is known, it advances
        // for real.
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

    /// <summary>
    /// "7,2 / 11,3 Mo" (7.2 / 11.3 MB), in the region's formats.
    /// </summary>
    private static string Weight(ProvisioningProgress step, ExternalDependency dependency)
    {
        const double Megabyte = 1024 * 1024;

        // The expected size is declared in the manifest: it serves
        // as a fallback when the server does not announce it in its
        // response.
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

    /// <summary>
    /// A component and its progress, as the window shows them.
    /// </summary>
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
