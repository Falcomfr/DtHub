using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.Mac.Views;

/// <summary>
/// A component and its progress, as <see cref="PreparationWindow" />
/// shows them: the PC's PreparationWindow.ToolRow.
/// </summary>
public sealed partial class PreparationRow : ObservableObject
{
    public required string Name { get; init; }

    public required string Host { get; init; }

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private double _fraction;

    [ObservableProperty]
    private bool _isBusy;
}
