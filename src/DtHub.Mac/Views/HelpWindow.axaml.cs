using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using DtHub.App.ViewModels;
using DtHub.Mac.Pc;

using Microsoft.Extensions.DependencyInjection;

namespace DtHub.Mac.Views;

/// <summary>
/// Help: the steps to follow on the phone, adapted to its brand. The PC's
/// HelpWindow (src/DtHub.App/Windows/HelpWindow.xaml.cs).
/// </summary>
public partial class HelpWindow : Window
{
    private readonly HelpViewModel _viewModel = null!;
    private bool _toFixes;

    /// <summary>For the XAML previewer only.</summary>
    public HelpWindow() => AvaloniaXamlLoader.Load(this);

    public HelpWindow(HelpViewModel viewModel)
    {
        _viewModel = viewModel;

        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;

        Opened += async (_, _) =>
        {
            await _viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);

            // Opened because nothing showed up: the fixes sit under the
            // three steps, out of sight. Only once the brand is settled,
            // since its warning moves everything below it, and laid out.
            if (_toFixes)
            {
                Dispatcher.UIThread.Post(ScrollToFixes, DispatcherPriority.Loaded);
            }
        };
    }

    /// <summary>
    /// Opens the steps on this phone's brand instead of guessing from
    /// whoever is answering. Called before showing.
    /// </summary>
    public void OpenFor(string? manufacturer) => _viewModel.OpenedFor = manufacturer;

    /// <summary>
    /// Opens on the cable's steps or on the network's, with the fixes in
    /// sight when the help is opened because something went wrong.
    /// </summary>
    public void OpenOn(bool cable, bool fixes = false)
    {
        _viewModel.OnCable = cable;
        _toFixes = fixes;
    }

    private void ScrollToFixes()
    {
        if (this.FindControl<ScrollViewer>("Scroller") is { } scroller
            && this.FindControl<Control>("Fixes") is { } fixes
            && this.FindControl<Control>("Steps") is { } steps
            && fixes.TranslatePoint(default, steps) is { } top)
        {
            scroller.Offset = new Vector(0, top.Y);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnSleepHelp(object? sender, RoutedEventArgs e)
    {
        var help = MacApp.Services.GetRequiredService<SleepHelpWindow>();
        _ = await Modal.ShowOver<object>(help, this).ConfigureAwait(true);
    }
}
