using System.Windows.Controls;

namespace DtHub.App.Windows;

/// <summary>
/// Liste des téléphones et de leurs instances. Le contexte de données attendu
/// est un <see cref="ViewModels.InstanceListViewModel"/>, sauf pour la commande
/// de relance, qui vient du configurateur.
/// </summary>
public partial class InstanceListControl : UserControl
{
    public InstanceListControl() => InitializeComponent();
}
