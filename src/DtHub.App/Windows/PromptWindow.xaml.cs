using System.Windows;
using System.Windows.Input;

namespace DtHub.App.Windows;

/// <summary>
/// Asks for a line of text. There was none: the dialog service
/// could only inform, warn and confirm.
///
/// Deliberately tiny, and without a view model: a window that asks
/// a question and returns an answer has no state to hold.
/// </summary>
public partial class PromptWindow : Window
{
    public PromptWindow(string question, string? initial, string? details = null, string? acceptLabel = null)
    {
        InitializeComponent();

        Question.Text = question;
        Entry.Text = initial ?? string.Empty;

        // The explanatory box only appears if the caller has
        // something to announce: an empty box would cost height
        // for nothing.
        if (!string.IsNullOrWhiteSpace(details))
        {
            Details.Text = details;
            DetailsBox.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(acceptLabel))
        {
            Accept.Content = acceptLabel;
        }

        // Everything is selected: the most frequent response to a
        // suggested value is to replace it, not to complete it.
        Loaded += (_, _) =>
        {
            Entry.Focus();
            Entry.SelectAll();
        };
    }

    /// <summary>The entered text, once the window is accepted.</summary>
    public string Answer { get; private set; } = string.Empty;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Answer = Entry.Text;
        DialogResult = true;
    }

    /// <summary>
    /// Enter validates, like the default button. The handler
    /// exists because the key must work from the field itself,
    /// where the default button does not always receive the
    /// keystroke.
    /// </summary>
    private void OnEntryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAccept(sender, e);
            e.Handled = true;
        }
    }
}
