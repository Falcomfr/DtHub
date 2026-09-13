using System.Windows;
using System.Windows.Controls;

using DtHub.Core.Guidance;

namespace DtHub.App.Windows;

/// <summary>
/// Shows a menu path as the sequence of screens it describes, with
/// the real labels inside.
///
/// Nothing is written specifically for it: brand sheets already
/// write their paths "Paramètres › Applications › DOFUS Touch", and
/// it is this sequence that the view draws. A path whose label
/// changes therefore changes what is drawn without anyone touching
/// this file.
/// </summary>
public partial class MenuPathControl : UserControl
{
    public MenuPathControl() => InitializeComponent();

    /// <summary>
    /// The path to show, exactly as the brand sheet writes it.
    /// </summary>
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
    /// The space left for a silent row's bar, once the badge, the
    /// chevron and the margins are removed. The template fixes the
    /// screen's width, so this is computed here rather than guessed.
    /// </summary>
    private const double BarWidth = 100;

    /// <summary>
    /// The badge tints, by their palette key. The core returns their
    /// rank, the view their color: it is the theme that decides
    /// colors, here as everywhere.
    /// </summary>
    private static readonly string[] Teintes =
        ["MenuTintA", "MenuTintB", "MenuTintC", "MenuTintD", "MenuTintE", "MenuTintF"];

    private static Ligne Muette(string title, int rang)
    {
        var decor = MenuPath.Decor(title, rang);

        return new Ligne(
            string.Empty,
            false,
            Math.Round(BarWidth * decor.BarShare),
            // The subtitle is noticeably shorter than the title: that
            // is what makes it read as a subtitle rather than a
            // second line of the same text.
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

    /// <summary>
    /// A drawn screen: its title, its rows, and what follows it.
    /// </summary>
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
    /// A row of the drawn list. Only the one you tap carries a
    /// label: inventing the text of the other menu rows would show
    /// what the sheet does not say.
    /// </summary>
    /// <param name="Libelle">
    /// The real label, or nothing for a silent row.
    /// </param>
    /// <param name="EstCelleQuOnTouche">True for the row to tap.</param>
    /// <param name="Largeur">Length of the bar, for a silent row.</param>
    /// <param name="LargeurSousTitre">
    /// Length of the second bar, if there is one.
    /// </param>
    /// <param name="Teinte">Palette key of the badge.</param>
    /// <param name="AvecInterrupteur">
    /// True when the row carries a switch.
    /// </param>
    /// <param name="AvecSousTitre">
    /// True when the row carries a subtitle.
    /// </param>
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
        /// True when the row carries a silent chevron: neither the
        /// one you tap, which has its own in color, nor the one that
        /// toggles, which has its switch. The two would otherwise
        /// overlap.
        /// </summary>
        public bool AvecChevron => !EstCelleQuOnTouche && !AvecInterrupteur;
    }
}
