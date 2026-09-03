using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;
using DtHub.Core.Settings;

namespace DtHub.App.ViewModels;

/// <summary>
/// Éditeur de raccourcis, dans sa propre fenêtre. Le configurateur se contente
/// de les afficher : les modifier demande de capturer des touches, ce qui n'a
/// pas sa place dans une fenêtre qu'on garde ouverte pendant qu'on joue.
/// </summary>
public sealed partial class HotkeyEditorViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly GameLauncher _launcher;

    private HotkeySet _hotkeys = HotkeySet.Default;

    public HotkeyEditorViewModel(SettingsService settings, GameLauncher launcher)
    {
        _settings = settings;
        _launcher = launcher;
    }

    public ObservableCollection<HotkeyRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private HotkeyRowViewModel? _capturingRow;

    [ObservableProperty]
    private string? _problem;

    /// <summary>Signalé quand les raccourcis ont changé, pour rafraîchir l'affichage.</summary>
    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(true);
        Rebuild();
    }

    [RelayCommand]
    private void BeginCapture(HotkeyRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (CapturingRow is not null)
        {
            CapturingRow.IsCapturing = false;
        }

        row.Error = null;
        row.IsCapturing = true;
        CapturingRow = row;
    }

    [RelayCommand]
    private void CancelCapture()
    {
        if (CapturingRow is not null)
        {
            CapturingRow.IsCapturing = false;
        }

        CapturingRow = null;
    }

    [RelayCommand]
    private async Task RestoreDefaultsAsync(CancellationToken cancellationToken)
    {
        _hotkeys = HotkeySet.Default;
        Rebuild();

        await SaveAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Enregistre la combinaison capturée par la vue. Le refus est expliqué à
    /// l'endroit exact où l'utilisateur vient de taper.
    /// </summary>
    public async Task<bool> ApplyCapturedAsync(int virtualKey, HotkeyModifiers modifiers)
    {
        if (CapturingRow is not { } row)
        {
            return false;
        }

        var validation = _hotkeys.Validate(row.Action, virtualKey, modifiers);

        if (validation != HotkeyValidationResult.Valid)
        {
            row.Error = Describe(validation, _hotkeys.FindConflict(row.Action, virtualKey, modifiers));
            return false;
        }

        _hotkeys = _hotkeys.With(row.Action, virtualKey, modifiers);

        row.IsCapturing = false;
        row.Error = null;
        CapturingRow = null;

        Rebuild();
        await SaveAsync(CancellationToken.None).ConfigureAwait(true);

        return true;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _settings.SaveHotkeysAsync(_hotkeys, cancellationToken).ConfigureAwait(true);

        var refused = await _launcher.ReloadHotkeysAsync(cancellationToken).ConfigureAwait(true);

        Problem = refused.Count == 0
            ? null
            : Strings.Format(
                "HotkeyRefusedByWindows",
                string.Join(", ", refused.Select(HotkeyBinding.DescribeAction)));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var binding in _hotkeys.Bindings)
        {
            Rows.Add(new HotkeyRowViewModel(binding));
        }
    }

    private static string Describe(HotkeyValidationResult result, HotkeyAction? conflict) => result switch
    {
        HotkeyValidationResult.NoKey => Strings.Get("HotkeyNoKey"),
        HotkeyValidationResult.ModifierOnly => Strings.Get("HotkeyModifierOnly"),
        HotkeyValidationResult.MissingModifier => Strings.Get("HotkeyMissingModifier"),
        HotkeyValidationResult.ReservedBySystem => Strings.Get("HotkeyReserved"),
        HotkeyValidationResult.Duplicate when conflict is { } action =>
            Strings.Format("HotkeyDuplicateBy", HotkeyBinding.DescribeAction(action)),
        HotkeyValidationResult.Duplicate => Strings.Get("HotkeyDuplicate"),
        _ => Strings.Get("HotkeyRefused"),
    };
}
