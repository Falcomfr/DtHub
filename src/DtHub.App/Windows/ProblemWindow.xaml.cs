using System.Windows;

using DtHub.App.Services;
using DtHub.Core.Localization;

namespace DtHub.App.Windows;

/// <summary>
/// What is shown when something has failed.
///
/// The old dialog box displayed a sentence and the log folder
/// path, with a single "OK" button. The exception message never
/// reached the screen, the path could not be clicked, and there
/// was nothing to send to anyone.
///
/// This one shows the fault, lets the report be read before it is
/// copied, and opens the repository's report form. **Nothing
/// leaves from here**: the report goes to the clipboard, and it is
/// the person who decides. This is the same line the report to
/// papycha.fr already follows.
/// </summary>
public partial class ProblemWindow : Window
{
    private readonly IDialogService _dialogs;
    private readonly string _report;
    private readonly string _headline;

    public ProblemWindow(IDialogService dialogs, string headline, string report, string? detail)
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        InitializeComponent();

        _dialogs = dialogs;
        _report = report;
        _headline = headline;

        // The title follows what is shown: "a problem occurred"
        // would lie when it is the person who comes to report
        // something about themselves.
        Title = headline;
        Headline.Text = headline;
        Report.Text = report;

        Detail.Text = detail ?? string.Empty;
        Detail.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        _dialogs.CopyToClipboard(_report);
        Copied.Visibility = Visibility.Visible;
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) =>
        _dialogs.OpenFolder(Logs);

    private void OnReport(object sender, RoutedEventArgs e)
    {
        // The report goes to the clipboard before opening: the
        // form asks for it to be pasted, and already having it at
        // hand avoids a back and forth between two windows.
        _dialogs.CopyToClipboard(_report);
        Copied.Visibility = Visibility.Visible;

        _dialogs.OpenUrl(DiagnosticReporter.IssueUrl(_headline));
    }

    /// <summary>The logs folder, set by the caller.</summary>
    public required string Logs { get; init; }

}
