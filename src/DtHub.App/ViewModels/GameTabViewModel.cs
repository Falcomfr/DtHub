using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// A frame tab: the account it shows, and the window it hosts.
/// </summary>
public sealed partial class GameTabViewModel : ObservableObject
{
    public GameTabViewModel(string key, string title, string? iconPath, nint window, double aspect)
    {
        Key = key;
        _title = title;
        _iconPath = iconPath;
        Window = window;
        Aspect = aspect;
    }

    /// <summary>
    /// Instance key. Identifies the tab: it does not change.
    /// </summary>
    public string Key { get; }

    /// <summary>Docked game window, as Windows designates it.</summary>
    public nint Window { get; }

    /// <summary>
    /// Width-to-height ratio of the display, the one scrcpy locks.
    ///
    /// It is what gives the frame its shape: a hosting area of a
    /// different shape would leave a black band on the sides.
    /// </summary>
    public double Aspect { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string? _iconPath;

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
