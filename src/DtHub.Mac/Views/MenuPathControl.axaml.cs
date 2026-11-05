using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using DtHub.Core.Guidance;

namespace DtHub.Mac.Views;

/// <summary>
/// Shows a menu path as the sequence of screens it describes, with the
/// real labels inside: the PC's MenuPathControl
/// (src/DtHub.App/Windows/MenuPathControl.xaml.cs). Brand sheets already
/// write their paths "Paramètres › Applications › DOFUS Touch", and that
/// sequence is what is drawn.
/// </summary>
public partial class MenuPathControl : UserControl
{
    /// <summary>The path to show, exactly as the brand sheet writes it.</summary>
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<MenuPathControl, string?>(nameof(Path));

    /// <summary>
    /// The space left for a silent row's bar, once the badge, the chevron
    /// and the margins are removed: the screen's width is fixed.
    /// </summary>
    private const double BarWidth = 100;

    /// <summary>The badge tints, by their palette key: the theme decides colours.</summary>
    private static readonly string[] Tints =
        ["MenuTintA", "MenuTintB", "MenuTintC", "MenuTintD", "MenuTintE", "MenuTintF"];

    public MenuPathControl()
    {
        AvaloniaXamlLoader.Load(this);
        Rebuild();
    }

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PathProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (this.FindControl<ItemsControl>("Screens") is not { } list)
        {
            return;
        }

        var screens = MenuPath.Screens(Path);

        list.ItemsSource = screens
            .Select((screen, index) => new MenuScreenView(screen, index < screens.Count - 1))
            .ToList();
    }

    internal static MenuRowView Silent(string title, int rank)
    {
        var decor = MenuPath.Decor(title, rank);

        return new MenuRowView(
            string.Empty,
            false,
            Math.Round(BarWidth * decor.BarShare),
            // The subtitle is noticeably shorter than the title: that is
            // what makes it read as a subtitle.
            Math.Round(BarWidth * decor.BarShare * 0.62),
            Tints[decor.Tint % Tints.Length],
            decor.HasSwitch,
            decor.HasSubtitle);
    }

    internal static MenuRowView ToTap(string label) =>
        new(label, true, 0, 0, Tints[0], false, false);
}

/// <summary>A drawn screen: its title, its rows, and whether another follows it.</summary>
public sealed class MenuScreenView
{
    internal MenuScreenView(MenuScreen screen, bool followed)
    {
        Title = screen.Title;
        IsFollowed = followed;

        Rows =
        [
            .. Enumerable.Range(0, MenuPath.Rows).Select(rank =>
                rank == screen.Row && screen.Tap.Length > 0
                    ? MenuPathControl.ToTap(screen.Tap)
                    : MenuPathControl.Silent(screen.Title, rank)),
        ];
    }

    public string Title { get; }

    public bool IsFollowed { get; }

    public IReadOnlyList<MenuRowView> Rows { get; }
}

/// <summary>
/// A row of the drawn list. Only the one to tap carries a label: inventing
/// the other rows' text would show what the sheet does not say.
/// </summary>
/// <param name="Label">The real label, or nothing for a silent row.</param>
/// <param name="IsTapped">True for the row to tap.</param>
/// <param name="BarWidth">Length of the bar, for a silent row.</param>
/// <param name="SubtitleWidth">Length of the second bar, if there is one.</param>
/// <param name="Tint">Palette key of the badge.</param>
/// <param name="HasSwitch">True when the row carries a switch.</param>
/// <param name="HasSubtitle">True when the row carries a subtitle.</param>
public sealed record MenuRowView(
    string Label,
    bool IsTapped,
    double BarWidth,
    double SubtitleWidth,
    string Tint,
    bool HasSwitch,
    bool HasSubtitle)
{
    /// <summary>
    /// A silent chevron: neither on the row to tap, which has its own in
    /// colour, nor on the one that toggles, which has its switch.
    /// </summary>
    public bool HasChevron => !IsTapped && !HasSwitch;
}
