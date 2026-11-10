using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.IPhone;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>One iPhone or iPad in the device list, with its Open/Close button (D227).</summary>
public sealed partial class IPhoneRowViewModel(IPhoneDevice device) : ObservableObject
{
    public IPhoneDevice Device { get; } = device;

    public string Label => LabelOf(Device);

    /// <summary>The device's line, shared with the iPhone window.</summary>
    public static string LabelOf(IPhoneDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return Strings.Format(device.CanMirror ? "IPhoneRow" : "IPhoneRowTooOld", device.Name, device.IosVersion, device.SystemName);
    }

    /// <summary>For a device below version 27: what it has, what it needs, how to update; null otherwise.</summary>
    public static string? TooOldHelp(IPhoneDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return device.CanMirror ? null : Strings.Format("IPhoneTooOldHelp", device.SystemName, device.IosVersion);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Action))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    private bool _isBusy;

    public bool CanAct => Device.CanMirror && !IsBusy;

    /// <summary>What went wrong at the last opening, or why the device cannot be opened, said to the player.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    // A device too old says so from the start: a greyed button alone did not say why (iPad, 2026-11-10).
    private string? _problem = TooOldHelp(device);

    public bool HasProblem => Problem is not null;

    public string Action => Strings.Get(IsOpen ? "Close" : "IPhoneOpen");
}
