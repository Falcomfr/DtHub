using System.Windows;
using System.Windows.Controls;

using DtHub.Core.Guidance;

namespace DtHub.App.Windows;

/// <summary>
/// Montre un chemin de menu comme la suite d'écrans qu'il décrit.
///
/// Rien n'est rédigé pour lui : les fiches de marque écrivent déjà leurs
/// chemins « Paramètres › Applications › DOFUS Touch », et c'est cette suite
/// que la vue dessine. Un chemin qui change de libellé change donc de dessin
/// sans qu'on ait à y toucher.
/// </summary>
public partial class MenuPathControl : UserControl
{
    public MenuPathControl() => InitializeComponent();

    /// <summary>Le chemin à montrer, tel que la fiche de marque l'écrit.</summary>
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path),
        typeof(string),
        typeof(MenuPathControl),
        new PropertyMetadata(null, OnPathChanged));

    public string? Path
    {
        get => (string?)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    private static void OnPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuPathControl view)
        {
            view.Rebuild();
        }
    }

    private void Rebuild()
    {
        var steps = MenuPath.Steps(Path);

        Ecrans.ItemsSource = steps
            .Select((step, index) => new Ecran(step, index < steps.Count - 1))
            .ToList();
    }

    /// <summary>Un écran dessiné : son libellé, ses lignes, et ce qui le suit.</summary>
    internal sealed record Ecran
    {
        public Ecran(MenuStep step, bool followed)
        {
            Libelle = step.Label;
            SuitQuelqueChose = followed;
            Lignes = [.. Enumerable.Range(0, MenuPath.Rows).Select(r => new Ligne(r == step.Row))];
        }

        public string Libelle { get; }

        public bool SuitQuelqueChose { get; }

        public IReadOnlyList<Ligne> Lignes { get; }
    }

    /// <summary>Une ligne de la liste dessinée.</summary>
    internal sealed record Ligne(bool EstSurlignee);
}
