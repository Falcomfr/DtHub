using System.Windows;
using System.Windows.Threading;

using DtHub.App.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace DtHub.App.Windows;

/// <summary>
/// Help: the steps to follow on the phone, adapted to its brand.
/// </summary>
public partial class HelpWindow : Window
{
    private readonly HelpViewModel _viewModel;
    private bool _toFixes;

    public HelpWindow(HelpViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);

            // Opened because nothing showed up: the fixes sit under the three
            // steps, out of sight. Only once the brand is settled, since its
            // warning moves everything below it, and once laid out again.
            if (_toFixes)
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                    Scroller.ScrollToVerticalOffset(Fixes.TranslatePoint(default, Steps).Y));
            }
        };
    }

    /// <summary>
    /// Opens the steps on this phone's brand instead of guessing from
    /// whoever is answering. Called before showing, since the brand is
    /// settled when the window loads.
    /// </summary>
    public void OpenFor(string? manufacturer) => _viewModel.OpenedFor = manufacturer;

    /// <summary>
    /// Opens on the cable's steps or on the network's, with the fixes
    /// in sight when the help is opened because something went wrong.
    /// </summary>
    public void OpenOn(bool cable, bool fixes = false)
    {
        _viewModel.OnCable = cable;
        _toFixes = fixes;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnSleepHelp(object sender, RoutedEventArgs e)
    {
        var help = AppHost.Services.GetRequiredService<SleepHelpWindow>();
        help.Owner = this;
        _ = help.ShowDialog();
    }
}
