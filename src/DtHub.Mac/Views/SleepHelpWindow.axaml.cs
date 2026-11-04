using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using DtHub.App.ViewModels;

namespace DtHub.Mac.Views;

/// <summary>
/// Explains how to keep Android from putting the game to sleep. The PC's
/// SleepHelpWindow (src/DtHub.App/Windows/SleepHelpWindow.xaml.cs).
/// </summary>
public partial class SleepHelpWindow : Window
{
    private readonly SleepHelpViewModel _viewModel = null!;

    /// <summary>For the XAML previewer only.</summary>
    public SleepHelpWindow() => AvaloniaXamlLoader.Load(this);

    public SleepHelpWindow(SleepHelpViewModel viewModel)
    {
        _viewModel = viewModel;

        AvaloniaXamlLoader.Load(this);

        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>The view model, for the alert line to name the app and the phone before showing.</summary>
    public SleepHelpViewModel ViewModel => _viewModel;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnMute(object? sender, RoutedEventArgs e)
    {
        await _viewModel.MuteAsync().ConfigureAwait(true);
        Close();
    }

    private async void OnUnmute(object? sender, RoutedEventArgs e) =>
        await _viewModel.UnmuteAllAsync().ConfigureAwait(true);
}
