using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// A guide step, as it appears in the list where it is chosen.
///
/// The two arrows advance one step at a time: on a guide with
/// twelve of them, going back to the third one required nine
/// clicks, and nothing said what would be found along the way.
/// </summary>
public sealed partial class QuestStepRowViewModel : ObservableObject
{
    public QuestStepRowViewModel(int index, string rank, string label)
    {
        Index = index;
        Rank = rank;
        Label = label;
    }

    /// <summary>Step rank, counted from zero as on the page.</summary>
    public int Index { get; }

    /// <summary>
    /// What is read to the left of the line: a number, or "Start".
    ///
    /// Given, not derived from the rank, because the start is not
    /// numbered: it is not a step of the route but the place one
    /// goes to begin it.
    /// </summary>
    public string Rank { get; }

    /// <summary>
    /// What there is to do there, when there is something to say
    /// about it.
    /// </summary>
    public string Label { get; }

    /// <summary>True for the step currently on.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
