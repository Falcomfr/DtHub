using System.Windows;

namespace DtHub.App.Views;

/// <summary>Boîte de saisie d'un texte court : nom d'appareil ou de profil.</summary>
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string prompt, string current)
    {
        InitializeComponent();

        Title = prompt;
        PromptText.Text = prompt;
        Input.Text = current;

        Loaded += (_, _) =>
        {
            Input.SelectAll();
            _ = Input.Focus();
        };
    }

    /// <summary>Texte saisi, une fois la boîte validée.</summary>
    public string Value { get; private set; } = string.Empty;

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        Value = Input.Text.Trim();

        if (Value.Length == 0)
        {
            return;
        }

        DialogResult = true;
    }
}
