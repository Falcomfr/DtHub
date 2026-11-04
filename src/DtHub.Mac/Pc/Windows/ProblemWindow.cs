using Avalonia.Controls;
using Avalonia.Layout;

using DtHub.App.Services;
using DtHub.Core;
using DtHub.Core.Localization;

namespace DtHub.App.Windows;

/// <summary>
/// The PC's "report a problem" window: the report, to copy, to send on
/// GitHub or Discord with it already in the clipboard, and the logs.
/// </summary>
public sealed class ProblemWindow : Window
{
    private readonly IDialogService _dialogs;
    private readonly string _report;
    private readonly string _headline;
    private readonly TextBlock _copied = new() { Text = Strings.Get("ReportCopied"), IsVisible = false };

    public ProblemWindow(IDialogService dialogs, string headline, string report, string? detail)
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        _dialogs = dialogs;
        _report = report;
        _headline = headline;
        Title = headline;
        Width = 560;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Key("CopyReport", OnCopy));
        buttons.Children.Add(Key("ReportProblem", OnReport));
        buttons.Children.Add(Key("ReportOnDiscord", OnDiscord));
        buttons.Children.Add(Key("OpenLogFolder", OnOpenLogs));
        buttons.Children.Add(Key("Close", Close));

        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(new TextBlock
        {
            Text = headline,
            FontSize = 17,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(detail))
        {
            top.Children.Add(new TextBlock { Text = detail, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }

        top.Children.Add(new TextBlock { Text = Strings.Get("ReportContents"), Opacity = 0.7 });
        DockPanel.SetDock(top, Dock.Top);

        var bottom = new StackPanel { Spacing = 6, Margin = new Avalonia.Thickness(0, 10, 0, 0) };
        bottom.Children.Add(_copied);
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);

        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(18),
            Children =
            {
                top,
                bottom,
                new TextBox
                {
                    Text = report,
                    IsReadOnly = true,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontFamily = new Avalonia.Media.FontFamily("Menlo"),
                    FontSize = 11,
                    Margin = new Avalonia.Thickness(0, 6, 0, 0),
                },
            },
        };
    }

    /// <summary>The logs folder, set by the caller.</summary>
    public required string Logs { get; init; }

    /// <summary>WPF's blocking ShowDialog, which the shared code calls.</summary>
    public bool? ShowDialog() => DtHub.Mac.Pc.Modal.Show<bool?>(this);

    private static Button Key(string text, Action act)
    {
        var button = new Button { Content = Strings.Get(text), Margin = new Avalonia.Thickness(0, 0, 6, 6) };
        button.Click += (_, _) => act();
        return button;
    }

    private void OnOpenLogs() => _dialogs.OpenFolder(Logs);

    private void OnCopy()
    {
        _dialogs.CopyToClipboard(_report);
        _copied.IsVisible = true;
    }

    private void OnReport()
    {
        // In the clipboard before opening: the form asks for it to be
        // pasted.
        OnCopy();
        _dialogs.OpenUrl(DiagnosticReporter.IssueUrl(_headline));
    }

    private void OnDiscord()
    {
        OnCopy();
        _dialogs.OpenUrl(ProductInfo.DiscordUrl);
    }
}
