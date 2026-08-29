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

    /// <summary>
    /// Montre la case « ouvrir au démarrage ». Vraie seulement dans la fenêtre
    /// de premier lancement : c'est le dernier endroit où la question se pose.
    /// Ensuite, ce qui rouvre est ce qui était ouvert au moment de quitter.
    /// </summary>
    public static readonly DependencyProperty ShowSelectionProperty =
        DependencyProperty.Register(
            nameof(ShowSelection),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(false));

    /// <summary>
    /// Montre les commandes de réordonnancement. Absentes de la fenêtre de
    /// premier lancement, où rien n'est encore ouvert.
    /// </summary>
    public static readonly DependencyProperty ShowOrderingProperty =
        DependencyProperty.Register(
            nameof(ShowOrdering),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(true));

    public InstanceListControl() => InitializeComponent();

    public bool ShowSelection
    {
        get => (bool)GetValue(ShowSelectionProperty);
        set => SetValue(ShowSelectionProperty, value);
    }

    public bool ShowOrdering
    {
        get => (bool)GetValue(ShowOrderingProperty);
        set => SetValue(ShowOrderingProperty, value);
    }

    public bool ShowActions
    {
        get => (bool)GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }
}
