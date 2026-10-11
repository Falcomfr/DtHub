using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>A row of the hotkey editor.</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    public HotkeyRowViewModel(HotkeyBinding binding) => _binding = binding;

    [ObservableProperty]
    private HotkeyBinding _binding;

    /// <summary>True while capturing the new combination.</summary>
    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>Message explaining why a combination was refused.</summary>
    [ObservableProperty]
    private string? _error;

    public HotkeyAction Action => Binding.Action;

    public string ActionLabel => HotkeyBinding.DescribeAction(Binding.Action);

    /// <summary>What the action actually does, for the tooltip.</summary>
    public string ActionDetail => HotkeyBinding.DetailAction(Binding.Action);

    public string ShortcutText => IsCapturing ? Strings.Get("HotkeyPressNew") : Binding.DisplayText;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>
    /// A key alone: it no longer reaches the game while a DT Hub window is in
    /// front, which the row says under the shortcut.
    /// </summary>
    public bool TakesKeyFromGame => Binding.IsAssigned && Binding.Modifiers == HotkeyModifiers.None && !HasError;

    partial void OnBindingChanged(HotkeyBinding value)
    {
        OnPropertyChanged(nameof(ShortcutText));
        OnPropertyChanged(nameof(TakesKeyFromGame));
    }

    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(ShortcutText));

    partial void OnErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(TakesKeyFromGame));
    }
}
