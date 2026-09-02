using System.Windows;
using System.Windows.Input;

namespace DtHub.App.Windows;

/// <summary>
/// Demande une ligne de texte. Il n'y en avait aucune : le service de dialogues
/// ne savait qu'informer, avertir et faire confirmer.
///
/// Volontairement minuscule, et sans vue-modèle : une fenêtre qui pose une
/// question et rend une réponse n'a pas d'état à tenir.
/// </summary>
public partial class PromptWindow : Window
{
    public PromptWindow(string question, string? initial)
    {
        InitializeComponent();

        Question.Text = question;
        Entry.Text = initial ?? string.Empty;

        // Tout est sélectionné : la réponse la plus fréquente à une valeur
        // proposée est de la remplacer, non de la compléter.
        Loaded += (_, _) =>
        {
            Entry.Focus();
            Entry.SelectAll();
        };
    }

    /// <summary>Le texte saisi, une fois la fenêtre acceptée.</summary>
    public string Answer { get; private set; } = string.Empty;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Answer = Entry.Text;
        DialogResult = true;
    }

    /// <summary>
    /// Entrée valide, comme le bouton par défaut. Le gestionnaire existe parce
    /// que la touche doit fonctionner depuis le champ lui-même, où le bouton
    /// par défaut ne reçoit pas toujours la frappe.
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
