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
    /// la pastille, le chevron et les marges. Le gabarit fixe la largeur de
    /// l'écran, donc celle-ci se calcule ici plutôt que de se deviner.
    /// </summary>
    private const double BarWidth = 100;

    /// <summary>
    /// Les teintes de pastille, par leur clé de palette. Le noyau en rend le
    /// rang, la vue la couleur : c'est le thème qui décide des couleurs, ici
    /// comme partout.
    /// </summary>
    private static readonly string[] Teintes =
        ["MenuTintA", "MenuTintB", "MenuTintC", "MenuTintD", "MenuTintE", "MenuTintF"];

    private static Ligne Muette(string titre, int rang)
    {
        var decor = MenuPath.Decor(titre, rang);

        return new Ligne(
            string.Empty,
            false,
            Math.Round(BarWidth * decor.BarShare),
            // Le sous-titre est nettement plus court que le titre : c'est ce
            // qui le fait lire comme un sous-titre et non comme une seconde
            // ligne du même texte.
            Math.Round(BarWidth * decor.BarShare * 0.62),
            Teintes[decor.Tint % Teintes.Length],
            decor.HasSwitch,
            decor.HasSubtitle);
    }

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
                        ? new Ligne(screen.Tap, true, 0, 0, Teintes[0], false, false)
                        : Muette(screen.Title, rang)),
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
    /// <param name="LargeurSousTitre">Longueur de la seconde barre, s'il y en a une.</param>
    /// <param name="Teinte">Clé de palette de la pastille.</param>
    /// <param name="AvecInterrupteur">Vrai quand la ligne porte un interrupteur.</param>
    /// <param name="AvecSousTitre">Vrai quand la ligne porte un sous-titre.</param>
    internal sealed record Ligne(
        string Libelle,
        bool EstCelleQuOnTouche,
        double Largeur,
        double LargeurSousTitre,
        string Teinte,
        bool AvecInterrupteur,
        bool AvecSousTitre)
    {
        /// <summary>
        /// Vrai quand la ligne porte un chevron muet : ni celle qu'on touche,
        /// qui a le sien en couleur, ni celle qui bascule, qui a son
        /// interrupteur. Les deux se seraient superposés.
        /// </summary>
        public bool AvecChevron => !EstCelleQuOnTouche && !AvecInterrupteur;
    }
}
