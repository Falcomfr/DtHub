using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.IPhone;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>One iPhone or iPad in the device list, with its Open/Close button (D227).</summary>
public sealed partial class IPhoneRowViewModel(IPhoneDevice device) : ObservableObject
{
    public IPhoneDevice Device { get; } = device;

    public string Label => Strings.Format(Device.CanMirror ? "IPhoneRow" : "IPhoneRowTooOld", Device.Name, Device.IosVersion);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Action))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    private bool _isBusy;

    public bool CanAct => Device.CanMirror && !IsBusy;

    /// <summary>What went wrong at the last opening, said to the player.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string? _problem;

    public bool HasProblem => Problem is not null;

    public string Action => Strings.Get(IsOpen ? "Close" : "IPhoneOpen");
}
