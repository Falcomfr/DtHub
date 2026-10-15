using System.Windows;

using DtHub.App.Services;
using DtHub.Core.Localization;
using DtHub.Core.Updates;

using Microsoft.Extensions.DependencyInjection;

namespace DtHub.App.Windows;

/// <summary>
/// Shows what a version brings, as announced by the repository.
/// </summary>
public partial class UpdateWindow : Window
{
    public UpdateWindow(string heading, string lead, string notes)
    {
        InitializeComponent();

        DataContext = new { Heading = heading, Lead = lead, Notes = notes };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnAllVersions(object sender, RoutedEventArgs e) =>
        AppHost.Services.GetRequiredService<IDialogService>()
            .OpenUrl(ReleaseNotes.ChangelogUrl(Strings.Spoken.TwoLetterISOLanguageName));
}
