using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using DtHub.App.ViewModels;

namespace DtHub.Mac.Views;

/// <summary>
/// Chooses what a phone shows beside the game, or asks for a new account:
/// the PC's AppPickerWindow (src/DtHub.App/Windows/AppPickerWindow.xaml.cs).
/// Closes with true when an outcome was chosen, as the PC's DialogResult.
/// </summary>
public partial class AppPickerWindow : Window
{
    private readonly AppPickerViewModel _viewModel = null!;
    private readonly ItemsControl? _groups;

    /// <summary>For the XAML previewer only.</summary>
    public AppPickerWindow() => AvaloniaXamlLoader.Load(this);

    public AppPickerWindow(AppPickerViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);

        _viewModel = viewModel;
        DataContext = viewModel;
        _groups = this.FindControl<ItemsControl>("Groups");

        // The PC's list groups its view by profile; Avalonia has no
        // grouped view, so the groups are taken again on each change.
        viewModel.View.CollectionChanged += OnViewChanged;
        ShowGroups();

        // The search takes the focus: with a hundred applications on a
        // phone, typing a few letters is the way in.
        Opened += async (_, _) =>
        {
            this.FindControl<TextBox>("Search")?.Focus();
            await viewModel.LoadAsync().ConfigureAwait(true);
        };
    }

    private void OnViewChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        ShowGroups();

    private void ShowGroups()
    {
        if (_groups is not null)
        {
            _groups.ItemsSource = _viewModel.View.Groups;
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _viewModel.Outcome = AppPickerOutcome.Saved;
        Close(true);
    }

    private void OnClone(object? sender, RoutedEventArgs e)
    {
        _viewModel.Outcome = AppPickerOutcome.CloneRequested;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.View.CollectionChanged -= OnViewChanged;
        base.OnClosed(e);
    }
}
