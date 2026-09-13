using System.Windows;
using DtHub.Core;
using DtHub.Core.Localization;

namespace DtHub.App.Services;

/// <summary>
/// Dialog boxes, isolated behind an interface so that view models
/// do not call the graphical interface directly.
/// </summary>
public interface IDialogService
{
    void ShowInformation(string message, string? title = null);

    void ShowWarning(string message, string? title = null);

    bool Confirm(string message, string? title = null);

    /// <summary>Asks for a three-way answer, for the exit policy.</summary>
    bool? ConfirmWithCancel(string message, string? title = null);

    /// <summary>Opens the file explorer on a folder.</summary>
    void OpenFolder(string path);

    /// <summary>
    /// Asks where to write a file, or <c>null</c> if canceled.
    /// </summary>
    string? AskWhereToSave(string suggestedName, string filter, string title);

    /// <summary>
    /// Asks which file to read, or <c>null</c> if canceled.
    /// </summary>
    string? AskWhichFileToRead(string filter, string title);

    /// <summary>Opens an address in the default browser.</summary>
    void OpenUrl(string url);

    /// <summary>Places text on the Windows clipboard.</summary>
    void CopyToClipboard(string text);

    /// <summary>
    /// Asks for a line of text, or <c>null</c> if canceled.
    ///
    /// Returns the input as-is: it is up to the caller to decide
    /// what an empty or overly long text means for it.
    /// </summary>
    /// <param name="details">
    /// What the action will remember, announced above the field.
    /// Optional: a question that is self-sufficient does not need
    /// it.
    /// </param>
    /// <param name="acceptLabel">
    /// The button's word, "Save" by default.
    /// </param>
    string? PromptText(
        string question,
        string? initial = null,
        string? title = null,
        string? details = null,
        string? acceptLabel = null);
}

/// <summary>WPF implementation.</summary>
public sealed class DialogService : IDialogService
{
    public string? PromptText(
        string question,
        string? initial = null,
        string? title = null,
        string? details = null,
        string? acceptLabel = null)
    {
        var window = new Windows.PromptWindow(question, initial, details, acceptLabel)
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

    /// <inheritdoc />
    public string? AskWhereToSave(string suggestedName, string filter, string title)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggestedName,
            Filter = filter,
            Title = title,
            OverwritePrompt = true,
            AddExtension = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? AskWhichFileToRead(string filter, string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            Title = title,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void OpenFolder(string path)
    {
        if (!System.IO.Directory.Exists(path))
        {
            ShowWarning(Strings.Format("FolderDoesNotExistYet", path));
            return;
        }

        Start(path);
    }

    public void OpenUrl(string url)
    {
        // Only secure addresses are opened: nothing justifies
        // sending the user to plain text.
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
            // The clipboard is momentarily locked by another
            // program: that is not a reason to fail the action.
            ShowWarning(Strings.Get("ClipboardBusy"));
        }
    }

    private static void Start(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
        {
            UseShellExecute = true,
        });
}
