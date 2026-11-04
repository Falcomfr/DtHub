using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core;
using DtHub.Core.Localization;
using DtHub.Mac.Views;

using Serilog;

namespace DtHub.Mac.Pc;

/// <summary>
/// The PC's dialogs (DialogService in src/DtHub.App/Services/
/// IDialogService.cs) with Avalonia windows and the Mac's own folder,
/// browser and file pickers. Same answers, same guards: only HTTPS
/// addresses open, a missing folder is said rather than opened.
/// </summary>
internal sealed class MacDialogService : IDialogService
{
    public void ShowInformation(string message, string? title = null) =>
        _ = Ask(message, title, (Strings.Get("MacOk"), true));

    public void ShowWarning(string message, string? title = null) =>
        _ = Ask(message, title, (Strings.Get("MacOk"), true));

    public bool Confirm(string message, string? title = null) =>
        Ask(message, title, (Strings.Get("MacOk"), true), (Strings.Get("Cancel"), false)) == true;

    public bool? ConfirmWithCancel(string message, string? title = null) =>
        Ask(message, title, (Strings.Get("MacYes"), true), (Strings.Get("MacNo"), false), (Strings.Get("Cancel"), null));

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            ShowWarning(Strings.Format("FolderDoesNotExistYet", path));
            return;
        }

        Open(path);
    }

    public string? AskWhereToSave(string suggestedName, string filter, string title) =>
        Modal.Owner?.StorageProvider is not { } storage
            ? null
            : Modal.Wait(async () =>
            {
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = title,
                    SuggestedFileName = suggestedName,
                    FileTypeChoices = Types(filter),
                    ShowOverwritePrompt = true,
                }).ConfigureAwait(true);

                return file?.TryGetLocalPath();
            });

    public string? AskWhichFileToRead(string filter, string title) =>
        Modal.Owner?.StorageProvider is not { } storage
            ? null
            : Modal.Wait(async () =>
            {
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter = Types(filter),
                }).ConfigureAwait(true);

                return files.Count == 0 ? null : files[0].TryGetLocalPath();
            });

    public void OpenUrl(string url)
    {
        // Only secure addresses are opened: nothing justifies sending the
        // player to plain text.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            Log.Information("Ouvert dans le navigateur : {Hote}.", uri.Host);
            Open(uri.ToString());
        }
    }

    public void CopyToClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowWarning(Strings.Get("ClipboardBusy"));
        }
    }

    public string? PromptText(
        string question,
        string? initial = null,
        string? title = null,
        string? details = null,
        string? acceptLabel = null)
    {
        var window = new PromptWindow(question, initial, details, acceptLabel)
        {
            Title = title ?? ProductInfo.Name,
        };

        _ = Modal.Show<object>(window);
        return window.Answer;
    }

    /// <summary>The PC's: the outcome the window chose, cancelled when it was closed.</summary>
    public AppPickerOutcome ChooseApps(AppPickerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var window = new AppPickerWindow(viewModel);

        return Modal.Show<bool>(window) ? viewModel.Outcome : AppPickerOutcome.Cancelled;
    }

    private static bool? Ask(string message, string? title, params (string Label, bool? Answer)[] choices)
    {
        var window = Frame(title, out var buttons);
        bool? answer = choices.Length == 1 ? choices[0].Answer : null;

        var body = (StackPanel)((DockPanel)window.Content!).Children[1];
        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 460,
        });

        foreach (var (label, value) in choices.Reverse())
        {
            var button = new Button
            {
                Content = label,
                IsDefault = value == true,
                IsCancel = value is null || (value == false && choices.Length == 2),
            };
            button.Click += (_, _) =>
            {
                answer = value;
                window.Close();
            };
            buttons.Children.Add(button);
        }

        _ = Modal.Show<object>(window);
        return answer;
    }

    private static Window Frame(string? title, out StackPanel buttons)
    {
        buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        return new Window
        {
            Title = title ?? ProductInfo.Name,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Avalonia.Thickness(20),
                Children = { buttons, new StackPanel { Spacing = 10 } },
            },
        };
    }

    /// <summary>
    /// The PC's file filters ("Name|*.ext|Other|*.a;*.b") as the Mac's
    /// file types.
    /// </summary>
    private static List<FilePickerFileType> Types(string filter)
    {
        var parts = filter.Split('|');
        List<FilePickerFileType> types = [];

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            types.Add(new FilePickerFileType(parts[i]) { Patterns = parts[i + 1].Split(';') });
        }

        return types;
    }

    private static void Open(string target) =>
        Process.Start(new ProcessStartInfo("open") { ArgumentList = { target }, UseShellExecute = false })?.Dispose();
}
