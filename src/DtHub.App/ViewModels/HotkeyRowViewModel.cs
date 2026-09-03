using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>Une ligne de l'éditeur de raccourcis.</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    public HotkeyRowViewModel(HotkeyBinding binding) => _binding = binding;

    [ObservableProperty]
    private HotkeyBinding _binding;

    /// <summary>Vrai pendant la capture de la nouvelle combinaison.</summary>
    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>Message expliquant pourquoi une combinaison a été refusée.</summary>
    [ObservableProperty]
    private string? _error;

    public HotkeyAction Action => Binding.Action;

    public string ActionLabel => HotkeyBinding.DescribeAction(Binding.Action);

    /// <summary>Ce que l'action fait vraiment, pour l'infobulle.</summary>
    public string ActionDetail => HotkeyBinding.DetailAction(Binding.Action);

    public string ShortcutText => IsCapturing ? Strings.Get("HotkeyPressNew") : Binding.DisplayText;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    partial void OnBindingChanged(HotkeyBinding value) => OnPropertyChanged(nameof(ShortcutText));

    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(ShortcutText));

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
