using System.Windows;

namespace DtHub.App.Windows;

/// <summary>Montre ce qu'une version apporte, telle que le dépôt l'annonce.</summary>
public partial class UpdateWindow : Window
{
    public UpdateWindow(string heading, string lead, string notes)
    {
        InitializeComponent();

        DataContext = new { Heading = heading, Lead = lead, Notes = notes };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
