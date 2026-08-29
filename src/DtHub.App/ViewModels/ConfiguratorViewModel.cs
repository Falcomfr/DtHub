using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core;
using DtHub.Core.Hotkeys;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.App.ViewModels;

/// <summary>
/// Le configurateur : trois onglets, rien de plus. Chaque modification est
/// enregistrée immédiatement et appliquée aux fenêtres déjà ouvertes.
/// </summary>
public sealed partial class ConfiguratorViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly GameLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;

    private HotkeySet _hotkeys = HotkeySet.Default;
    private bool _loading;

    public ConfiguratorViewModel(
        InstanceListViewModel instances,
        PairingViewModel pairing,
        SettingsService settings,
        GameLauncher launcher,
        IDialogService dialogs,
        IAppPaths paths)
    {
        Instances = instances;
        Pairing = pairing;
        _settings = settings;
        _launcher = launcher;
        _dialogs = dialogs;
        _paths = paths;

        Pairing.Paired += async (_, _) => await Instances.RefreshAsync().ConfigureAwait(true);
    }

    public InstanceListViewModel Instances { get; }

    public PairingViewModel Pairing { get; }

    // Onglet Général

    /// <summary>Les neuf positions possibles du bloc de fenêtres.</summary>
    public IReadOnlyList<WindowAnchor> Anchors { get; } = WindowAnchors.All;

    [ObservableProperty]
    private WindowAnchor _gameAnchor = WindowAnchor.MiddleLeft;

    [ObservableProperty]
    private int _gameSizePercent = 70;

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];

    [ObservableProperty]
    private MonitorInfo? _preferredMonitor;

    /// <summary>Vrai s'il y a plus d'un écran : sinon le réglage est inutile.</summary>
    public bool HasSeveralMonitors => Monitors.Count > 1;

    // Onglet Raccourcis

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];

    [ObservableProperty]
    private HotkeyRowViewModel? _capturingRow;

    [ObservableProperty]
    private string? _hotkeyProblem;

    // Divers

    public string ProductName => ProductInfo.Name;

    public string Version => ProductInfo.Version;

    /// <summary>Raccourci d'affichage, rappelé en clair dans la fenêtre.</summary>
    public string ToggleShortcutText =>
        _hotkeys.For(HotkeyAction.ToggleConfigurator)?.DisplayText ?? "Ctrl + P";

    public string Disclaimer =>
        "Projet indépendant, sans lien avec Ankama, Genymobile, Google ni Xiaomi.";

    /// <summary>Charge l'état des réglages dans la fenêtre.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;

        try
        {
            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            GameAnchor = settings.GameAnchor;
            GameSizePercent = settings.GameSizePercent;

            Monitors.Clear();
            foreach (var monitor in _launcher.Monitors)
            {
                Monitors.Add(monitor);
            }

            PreferredMonitor = Monitors.FirstOrDefault(
                m => m.DeviceName == settings.PreferredMonitorDeviceName);

            OnPropertyChanged(nameof(HasSeveralMonitors));

            _hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(true);
            RebuildHotkeys();
        }
        finally
        {
            _loading = false;
        }

        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rafraîchit ce qui change tout seul : appareils et états.</summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
        Instances.RefreshRunningState();

        if (Instances.HasNoConnectedDevice)
        {
            await Pairing.ScanAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void SetAnchor(WindowAnchor anchor) => GameAnchor = anchor;

    [RelayCommand]
    private async Task RestartAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var report = await _launcher.RestartAsync(row.Instance).ConfigureAwait(true);
        Instances.RefreshRunningState();

        if (report.Problems.Count > 0)
        {
            Instances.Problem = string.Join(" ", report.Problems);
        }
    }

    [RelayCommand]
    private async Task StopAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _launcher.StopAsync(row.Instance).ConfigureAwait(true);
        Instances.RefreshRunningState();
    }

    [RelayCommand]
    private async Task LaunchEnabledAsync(CancellationToken cancellationToken)
    {
        var report = await _launcher.LaunchEnabledAsync(cancellationToken).ConfigureAwait(true);
        Instances.RefreshRunningState();

        Instances.Problem = report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null;
    }

    [RelayCommand]
    private async Task ArrangeAsync(CancellationToken cancellationToken)
    {
        var moved = await _launcher.ArrangeAsync(cancellationToken).ConfigureAwait(true);

        if (moved == 0)
        {
            Instances.Problem = "Aucune fenêtre de jeu à replacer.";
        }
    }

    [RelayCommand]
    private async Task CloseAllAsync(CancellationToken cancellationToken)
    {
        await _launcher.CloseAllAsync(cancellationToken).ConfigureAwait(true);
        Instances.RefreshRunningState();
    }

    [RelayCommand]
    private async Task ForgetDeviceAsync(DeviceGroupViewModel? group)
    {
        if (group is null
            || !_dialogs.Confirm(
                $"Oublier {group.Name} ?\n\nSes instances et leurs réglages seront effacés.",
                "Oublier le téléphone"))
        {
            return;
        }

        await _settings.ForgetDeviceAsync(group.DeviceId).ConfigureAwait(true);
        await Instances.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenLogs() => _dialogs.OpenFolder(_paths.LogsDirectory);

    // Raccourcis

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
    private async Task RestoreDefaultHotkeysAsync(CancellationToken cancellationToken)
    {
        _hotkeys = HotkeySet.Default;
        RebuildHotkeys();

        await _settings.SaveHotkeysAsync(_hotkeys, cancellationToken).ConfigureAwait(true);
        await ReportRefusedAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Enregistre la combinaison capturée par la vue. Le refus est expliqué à
    /// l'endroit exact où l'utilisateur vient de taper.
    /// </summary>
    public async Task<bool> ApplyCapturedHotkeyAsync(int virtualKey, HotkeyModifiers modifiers)
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

        RebuildHotkeys();

        await _settings.SaveHotkeysAsync(_hotkeys).ConfigureAwait(true);
        await ReportRefusedAsync().ConfigureAwait(true);

        return true;
    }

    private void RebuildHotkeys()
    {
        Hotkeys.Clear();
        foreach (var binding in _hotkeys.Bindings)
        {
            Hotkeys.Add(new HotkeyRowViewModel(binding));
        }

        OnPropertyChanged(nameof(ToggleShortcutText));
    }

    private async Task ReportRefusedAsync()
    {
        var refused = await _launcher.ReloadHotkeysAsync().ConfigureAwait(true);

        HotkeyProblem = refused.Count == 0
            ? null
            : "Refusé par Windows, probablement pris par un autre logiciel : "
              + string.Join(", ", refused.Select(HotkeyBinding.DescribeAction));
    }

    private static string Describe(HotkeyValidationResult result, HotkeyAction? conflict) => result switch
    {
        HotkeyValidationResult.NoKey => "Aucune touche saisie.",
        HotkeyValidationResult.ModifierOnly => "Ajoutez une touche en plus du modificateur.",
        HotkeyValidationResult.MissingModifier =>
            "Ajoutez Ctrl, Alt ou Maj, sinon la touche serait interceptée pendant que vous jouez.",
        HotkeyValidationResult.ReservedBySystem => "Cette combinaison est réservée par Windows.",
        HotkeyValidationResult.Duplicate when conflict is { } action =>
            $"Déjà utilisée par « {HotkeyBinding.DescribeAction(action)} ».",
        HotkeyValidationResult.Duplicate => "Cette combinaison est déjà utilisée.",
        _ => "Combinaison refusée.",
    };

    private void Save(Action<AppSettingsDocument> mutate)
    {
        if (_loading)
        {
            return;
        }

        _ = _settings.UpdateAsync(mutate);
    }

    partial void OnGameAnchorChanged(WindowAnchor value)
    {
        Save(s => s.GameAnchor = value);
        _ = _launcher.ArrangeAsync();
    }

    partial void OnGameSizePercentChanged(int value)
    {
        Save(s => s.GameSizePercent = value);
        _ = _launcher.ArrangeAsync();
    }

    partial void OnPreferredMonitorChanged(MonitorInfo? value)
    {
        Save(s => s.PreferredMonitorDeviceName = value?.DeviceName);
        _ = _launcher.ArrangeAsync();
    }
}
