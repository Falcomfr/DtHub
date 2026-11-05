using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using DtHub.App.Services;
using DtHub.Core.Localization;
using DtHub.Core.Updates;

using Microsoft.Extensions.DependencyInjection;

namespace DtHub.Mac.Views;

/// <summary>
/// Shows what a version brings: the PC's UpdateWindow
/// (src/DtHub.App/Windows/UpdateWindow.xaml.cs).
/// </summary>
public partial class UpdateWindow : Window
{
    /// <summary>For the XAML previewer only.</summary>
    public UpdateWindow() => AvaloniaXamlLoader.Load(this);

    public UpdateWindow(string heading, string lead, string notes)
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<TextBlock>("Heading")!.Text = heading;
        this.FindControl<TextBlock>("Lead")!.Text = lead;
        this.FindControl<SelectableTextBlock>("Notes")!.Text = notes;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnAllVersions(object? sender, RoutedEventArgs e) =>
        MacApp.Services.GetRequiredService<IDialogService>()
            .OpenUrl(ReleaseNotes.ChangelogUrl(Strings.Spoken.TwoLetterISOLanguageName));
}
