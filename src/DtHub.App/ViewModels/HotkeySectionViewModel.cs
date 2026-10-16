using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>
/// A heading of the shortcuts list and what goes under it: rows, and the
/// series folded into one line. Shared by the panel and the editor.
/// </summary>
public sealed record HotkeySectionViewModel(string Title, IReadOnlyList<object> Entries)
{
    /// <summary>
    /// The sections for a set of shortcuts, in <see cref="HotkeyLayout" />'s
    /// order.
    /// </summary>
    /// <param name="expanded">
    /// The series to show unfolded, by their first action: the editor
    /// rebuilds the list after every change, and a series being edited
    /// folded itself back under the cursor.
    /// </param>
    public static IReadOnlyList<HotkeySectionViewModel> Build(
        HotkeySet hotkeys, IReadOnlySet<HotkeyAction>? expanded = null)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        return
        [
            .. HotkeyLayout.Sections.Select(section => new HotkeySectionViewModel(
                HotkeyLayout.Title(section.Section),
                [
                    .. section.Entries
                        .Select(entry => entry.Select(hotkeys.For).OfType<HotkeyBinding>().ToList())
                        .Where(bindings => bindings.Count > 0)
                        .Select(bindings => bindings.Count == 1
                            ? (object)new HotkeyRowViewModel(bindings[0])
                            : new HotkeySeriesViewModel(bindings)
                            {
                                IsExpanded = expanded?.Contains(bindings[0].Action) == true,
                            }),
                ])),
        ];
    }
}

/// <summary>Accounts 1 to 8, or sizes 1 to 4, on one line that unfolds.</summary>
public sealed partial class HotkeySeriesViewModel : ObservableObject
{
    public HotkeySeriesViewModel(IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        Rows = [.. bindings.Select(b => new HotkeyRowViewModel(b))];
        Label = HotkeyLayout.SeriesLabel([.. bindings.Select(b => b.Action)]);
        Summary = HotkeyLayout.RunText(bindings) ?? Strings.Get("HotkeySeriesCustom");
    }

    public IReadOnlyList<HotkeyRowViewModel> Rows { get; }

    public HotkeyAction First => Rows[0].Action;

    public string Label { get; }

    /// <summary>"F1 … F8", or "Custom keys" once they no longer follow.</summary>
    public string Summary { get; }

    [ObservableProperty]
    private bool _isExpanded;
}
