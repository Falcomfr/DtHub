using System.Windows;
using System.Windows.Controls;

namespace DtHub.App.Windows;

/// <summary>
/// Liste des téléphones et de leurs instances. Le contexte de données attendu
/// est un <see cref="ViewModels.InstanceListViewModel"/>.
/// </summary>
public partial class InstanceListControl : UserControl
{
    /// <summary>
    /// Montre les boutons d'action de chaque instance. Coupé dans la fenêtre
    /// de mise en route : on y coche ce qui doit s'ouvrir, et c'est le bouton
    /// « Enregistrer et lancer » qui décide, pas un bouton par ligne.
    /// </summary>
    public static readonly DependencyProperty ShowActionsProperty =
        DependencyProperty.Register(
            nameof(ShowActions),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(true));

    public InstanceListControl() => InitializeComponent();

    public bool ShowActions
    {
        get => (bool)GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }
}
