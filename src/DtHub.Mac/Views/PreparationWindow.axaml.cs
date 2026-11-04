using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using DtHub.Core.Dependencies;
using DtHub.Core.Localization;
using DtHub.Mac.Platform;

namespace DtHub.Mac.Views;

/// <summary>
/// The first launch, when there is something to set up: the PC's
/// PreparationWindow (src/DtHub.App/Windows/PreparationWindow.xaml.cs).
/// It names what is missing, where it comes from, and shows the progress
/// the provisioner reports. It asks nothing: it informs, and closes
/// itself.
/// </summary>
public partial class PreparationWindow : Window
{
    private readonly MacToolPreparation? _preparation;
    private readonly IReadOnlyList<ExternalDependency> _missing = [];
    private readonly List<PreparationRow> _rows = [];
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _next;

    /// <summary>For the designer and the XAML loader only.</summary>
    public PreparationWindow() => AvaloniaXamlLoader.Load(this);

    internal PreparationWindow(MacToolPreparation preparation, IReadOnlyList<ExternalDependency> missing)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(missing);

        AvaloniaXamlLoader.Load(this);

        _preparation = preparation;
        _missing = missing;

        foreach (var dependency in missing)
        {
            _rows.Add(new PreparationRow
            {
                Name = $"{dependency.DisplayName} {dependency.Version}",
                Host = dependency.Url.Host,
                Status = Strings.Get("PreparationWaiting"),
            });
        }

        this.FindControl<ItemsControl>("Rows")!.ItemsSource = _rows;
    }

    /// <summary>Completed when everything is set up, or when the player gives up.</summary>
    public Task Completed => _completed.Task;

    /// <summary>
    /// Sets up what is missing, if anything is. Returns without showing
    /// anything when both tools are there, on every launch but the first.
    /// </summary>
    internal static async Task RunAsync(MacToolPreparation preparation)
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = WorkAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Closing by the red button counts as "continue": the application
        // lives without these tools, and nothing must be left waiting.
        _completed.TrySetResult();
        base.OnClosed(e);
    }

    private async Task WorkAsync()
    {
        if (_preparation is null)
        {
            return;
        }

        while (_next < _missing.Count)
        {
            var dependency = _missing[_next];
            var row = _rows[_next];

            row.IsBusy = true;
            row.Status = Strings.Get("PreparationDownloading");

            // Created on the interface thread: Progress<T> brings the
            // reports back here.
            var progress = new Progress<ProvisioningProgress>(step => Show(row, dependency, step));

            try
            {
                await _preparation.InstallAsync(dependency, progress).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A timed out download arrives as a cancellation, let
                // through by the provisioner: it must still offer the way out.
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

    private static void Show(PreparationRow row, ExternalDependency dependency, ProvisioningProgress step)
    {
        row.Status = step.Stage switch
        {
            ProvisioningStage.Downloading => Weight(step, dependency),
            ProvisioningStage.Verifying => Strings.Get("PreparationVerifying"),
            ProvisioningStage.Extracting => Strings.Get("PreparationExtracting"),
            _ => Strings.Get("PreparationDone"),
        };

        // The bar only spins while nobody knows where things stand.
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

    /// <summary>"7,2 / 11,3 Mo", in the region's formats.</summary>
    private static string Weight(ProvisioningProgress step, ExternalDependency dependency)
    {
        const double Megabyte = 1024 * 1024;

        // The manifest's size stands in when the server does not say.
        var total = step.TotalBytes ?? dependency.SizeBytes;

        return Strings.Format("PreparationBytes", step.BytesReceived / Megabyte, total / Megabyte);
    }

    private void Report(PreparationRow row, string message)
    {
        row.IsBusy = false;
        row.Fraction = 0;
        row.Status = Strings.Get("PreparationFailed");

        this.FindControl<TextBlock>("Footer")!.Text = message;
        this.FindControl<StackPanel>("Choices")!.IsVisible = true;
    }

    private void OnRetry(object? sender, RoutedEventArgs e)
    {
        this.FindControl<StackPanel>("Choices")!.IsVisible = false;
        this.FindControl<TextBlock>("Footer")!.Text = Strings.Get("PreparationChecksum");

        _ = WorkAsync();
    }

    private void OnSkip(object? sender, RoutedEventArgs e) => _completed.TrySetResult();
}
