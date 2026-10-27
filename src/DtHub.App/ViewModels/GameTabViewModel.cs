using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// A frame tab: the account it shows, and the window it hosts.
/// </summary>
public sealed partial class GameTabViewModel : ObservableObject
{
    public GameTabViewModel(
        string key, string title, string? iconPath, nint window, double aspect, string? colourBrushKey)
    {
        Key = key;
        _title = title;
        _iconPath = iconPath;
        Window = window;
        Aspect = aspect;
        _colourBrushKey = colourBrushKey;
    }

    /// <summary>
    /// The palette key of the account's mark, or <c>null</c> for none.
    ///
    /// The same mark the row wears, so the two surfaces teach one shape,
    /// on every tab, since it is the unselected tabs that a multi-account
    /// player needs to tell apart. The active tab's underline takes it too.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColour))]
    private string? _colourBrushKey;

    /// <summary>True when the account carries a mark.</summary>
    public bool HasColour => ColourBrushKey is not null;

    /// <summary>
    /// Instance key. Identifies the tab: it does not change.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Docked game window, as Windows designates it, or 0 while the account
    /// waits for its window to come back.
    /// </summary>
    public nint Window { get; set; }

    /// <summary>
    /// True while the account waits for its phone: the tab stays, and the
    /// frame shows the waiting view instead of the game.
    /// </summary>
    [ObservableProperty]
    private bool _isWaiting;

    /// <summary>True when the tab has a game to show.</summary>
    public bool ShowsGame => Window != 0 && !IsWaiting;

    /// <summary>
    /// Width-to-height ratio of the display, the one scrcpy locks.
    ///
    /// It is what gives the frame its shape: a hosting area of a
    /// different shape would leave a black band on the sides.
    /// </summary>
    public double Aspect { get; }

    [ObservableProperty]
    private string _title;

    /// <summary>
    /// What the frame's title says of the phone while this tab shows,
    /// or <c>null</c> when the player did not ask for it.
    /// </summary>
    public string? Status { get; set; }

    [ObservableProperty]
    private string? _iconPath;

    /// <summary>
    /// The key that brings this account forward, "F1" by default, or
    /// <c>null</c> when it has none. Shown on the tab so that the keys are
    /// learned where they are used.
    /// </summary>
    [ObservableProperty]
    private string? _accountKey;

    /// <summary>
    /// True for the tab shown. Only one is at a time: the other
    /// windows are hidden, not destroyed, so that returning to them
    /// is instant.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Where the dragged tab will land, shown by a line to the
    /// left or right of the one being hovered.
    ///
    /// Without this line, the drop was blind: nothing said on
    /// which side it would fall, and one had to try again just to
    /// understand.
    /// </summary>
    [ObservableProperty]
    private bool _dropBefore;

    [ObservableProperty]
    private bool _dropAfter;

    /// <summary>Clears both drop hints.</summary>
    public void ClearDropHint()
    {
        DropBefore = false;
        DropAfter = false;
    }
}
