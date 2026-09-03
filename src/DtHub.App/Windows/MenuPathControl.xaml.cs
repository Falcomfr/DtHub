using System.Windows;
using System.Windows.Controls;

using DtHub.Core.Guidance;

namespace DtHub.App.Windows;

/// <summary>
/// Montre un chemin de menu comme la suite d'écrans qu'il décrit, avec les
/// vrais libellés dedans.
///
/// Rien n'est rédigé pour lui : les fiches de marque écrivent déjà leurs
/// chemins « Paramètres › Applications › DOFUS Touch », et c'est cette suite
/// que la vue dessine. Un chemin qui change de libellé change donc de dessin
/// sans qu'on y touche.
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

    /// <summary>
    /// La place qui reste pour la barre d'une ligne muette, une fois retirés
    /// l'icône, le chevron et les marges. Le gabarit fixe la largeur de
    /// l'écran, donc celle-ci se calcule ici plutôt que de se deviner.
    /// </summary>
    private const double BarWidth = 100;

    private void Rebuild()
    {
        var screens = MenuPath.Screens(Path);

        Ecrans.ItemsSource = screens
            .Select((screen, index) => new Ecran(screen, index < screens.Count - 1))
            .ToList();
    }

    /// <summary>Un écran dessiné : son titre, ses lignes, et ce qui le suit.</summary>
    internal sealed class Ecran
    {
        public Ecran(MenuScreen screen, bool followed)
        {
            Titre = screen.Title;
            SuitQuelqueChose = followed;

            Lignes =
            [
                .. Enumerable.Range(0, MenuPath.Rows).Select(rang =>
                    rang == screen.Row && screen.Tap.Length > 0
                        ? new Ligne(screen.Tap, true, 0)
                        : new Ligne(
                            string.Empty,
                            false,
                            Math.Round(BarWidth * MenuPath.BarShare(screen.Title, rang)))),
            ];
        }

        public string Titre { get; }

        public bool SuitQuelqueChose { get; }

        public IReadOnlyList<Ligne> Lignes { get; }
    }

    /// <summary>
    /// Une ligne de la liste dessinée. Seule celle qu'on touche porte un
    /// libellé : inventer le texte des autres lignes du menu serait montrer ce
    /// que la fiche ne dit pas.
    /// </summary>
    /// <param name="Libelle">Le vrai libellé, ou rien pour une ligne muette.</param>
    /// <param name="EstCelleQuOnTouche">Vrai pour la ligne à toucher.</param>
    /// <param name="Largeur">Longueur de la barre, pour une ligne muette.</param>
    internal sealed record Ligne(string Libelle, bool EstCelleQuOnTouche, double Largeur);
}
