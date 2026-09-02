using System.Windows;

using DtHub.Core;

namespace DtHub.App.Services;

/// <summary>
/// Boîtes de dialogue, isolées derrière une interface pour que les vues-modèles
/// n'appellent pas directement l'interface graphique.
/// </summary>
public interface IDialogService
{
    void ShowInformation(string message, string? title = null);

    void ShowWarning(string message, string? title = null);

    bool Confirm(string message, string? title = null);

    /// <summary>Demande une réponse à trois branches, pour la politique de sortie.</summary>
    bool? ConfirmWithCancel(string message, string? title = null);

    /// <summary>Ouvre l'explorateur sur un dossier.</summary>
    void OpenFolder(string path);

    /// <summary>Ouvre une adresse dans le navigateur par défaut.</summary>
    void OpenUrl(string url);

    /// <summary>Place un texte dans le presse-papiers de Windows.</summary>
    void CopyToClipboard(string text);

    /// <summary>
    /// Demande une ligne de texte, ou <c>null</c> si l'on renonce.
    ///
    /// Rend la saisie telle quelle : c'est à l'appelant de décider ce qu'un
    /// texte vide ou trop long veut dire chez lui.
    /// </summary>
    string? PromptText(string question, string? initial = null, string? title = null);
}

/// <summary>Implémentation WPF.</summary>
public sealed class DialogService : IDialogService
{
    public string? PromptText(string question, string? initial = null, string? title = null)
    {
        var window = new Windows.PromptWindow(question, initial)
        {
            Title = title ?? ProductInfo.Name,
            Owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive && w.IsVisible),
        };

        return window.ShowDialog() == true ? window.Answer : null;
    }

    public void ShowInformation(string message, string? title = null) =>
        MessageBox.Show(message, title ?? ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string? title = null) =>
        MessageBox.Show(message, title ?? ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string? title = null) =>
        MessageBox.Show(message, title ?? ProductInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            == MessageBoxResult.OK;

    public bool? ConfirmWithCancel(string message, string? title = null) =>
        MessageBox.Show(message, title ?? ProductInfo.Name, MessageBoxButton.YesNoCancel, MessageBoxImage.Question)
            switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null,
            };

    public void OpenFolder(string path)
    {
        if (!System.IO.Directory.Exists(path))
        {
            ShowWarning($"Le dossier n'existe pas encore :\n{path}");
            return;
        }

        Start(path);
    }

    public void OpenUrl(string url)
    {
        // Seules les adresses sécurisées sont ouvertes : rien ne justifie
        // d'envoyer l'utilisateur sur du texte clair.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            Start(uri.ToString());
        }
    }

    public void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Le presse-papiers est momentanément verrouillé par un autre
            // logiciel : ce n'est pas une raison de faire échouer l'action.
            ShowWarning("Le presse-papiers est occupé par un autre logiciel. Réessayez dans un instant.");
        }
    }

    private static void Start(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
        {
            UseShellExecute = true,
        });
}
