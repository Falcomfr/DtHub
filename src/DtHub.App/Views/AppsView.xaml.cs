using System.Windows;
using System.Windows.Controls;

using DtHub.App.ViewModels;

namespace DtHub.App.Views;

/// <summary>Sélecteur d'applications.</summary>
public partial class AppsView : UserControl
{
    public AppsView() => InitializeComponent();

    /// <summary>
    /// Le nombre d'éléments cochés n'est pas déductible d'une liaison simple :
    /// la vue prévient la vue-modèle à chaque changement.
    /// </summary>
    private void OnSelectionChanged(object sender, RoutedEventArgs e) =>
        (DataContext as AppsViewModel)?.OnSelectionChanged();
}
