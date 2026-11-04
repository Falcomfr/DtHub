namespace DtHub.App.Services;

/// <summary>
/// The PC's dialog contract, word for word (src/DtHub.App/Services/
/// IDialogService.cs). The PC file also holds its WPF implementation, so it
/// cannot be compiled here; this copy keeps the shared view models talking
/// to <see cref="DtHub.Mac.Pc.MacDialogService"/> instead. A method added on
/// the PC breaks the Mac build until it is added here too.
/// </summary>
public interface IDialogService
{
    void ShowInformation(string message, string? title = null);

    void ShowWarning(string message, string? title = null);

    bool Confirm(string message, string? title = null);

    bool? ConfirmWithCancel(string message, string? title = null);

    void OpenFolder(string path);

    string? AskWhereToSave(string suggestedName, string filter, string title);

    string? AskWhichFileToRead(string filter, string title);

    void OpenUrl(string url);

    void CopyToClipboard(string text);

    string? PromptText(
        string question,
        string? initial = null,
        string? title = null,
        string? details = null,
        string? acceptLabel = null);

    ViewModels.AppPickerOutcome ChooseApps(ViewModels.AppPickerViewModel viewModel);
}
