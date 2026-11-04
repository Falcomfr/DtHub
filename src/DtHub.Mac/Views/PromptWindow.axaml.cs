using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DtHub.Mac.Views;

/// <summary>
/// Asks for a line of text: the PC's PromptWindow
/// (src/DtHub.App/Windows/PromptWindow.xaml.cs). No view model: a window
/// that asks a question and returns an answer has no state to hold.
/// </summary>
public partial class PromptWindow : Window
{
    private readonly TextBox _entry = null!;

    /// <summary>For the XAML previewer only.</summary>
    public PromptWindow() => AvaloniaXamlLoader.Load(this);

    public PromptWindow(string question, string? initial, string? details = null, string? acceptLabel = null)
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<TextBlock>("Question")!.Text = question;
        _entry = this.FindControl<TextBox>("Entry")!;
        _entry.Text = initial ?? string.Empty;

        // The explanatory box only appears when the caller has something
        // to announce.
        if (!string.IsNullOrWhiteSpace(details))
        {
            this.FindControl<TextBlock>("Details")!.Text = details;
            this.FindControl<Border>("DetailsBox")!.IsVisible = true;
        }

        if (!string.IsNullOrWhiteSpace(acceptLabel))
        {
            this.FindControl<Button>("Accept")!.Content = acceptLabel;
        }

        // Everything selected: the most frequent answer to a suggested
        // value is to replace it, not to complete it.
        Opened += (_, _) =>
        {
            _entry.Focus();
            _entry.SelectAll();
        };
    }

    /// <summary>The entered text, once the window is accepted; null otherwise.</summary>
    public string? Answer { get; private set; }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        Answer = _entry.Text ?? string.Empty;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Enter accepts from the field itself, like the default button.</summary>
    private void OnEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAccept(sender, e);
            e.Handled = true;
        }
    }
}
