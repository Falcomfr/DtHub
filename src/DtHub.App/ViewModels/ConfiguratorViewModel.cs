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
/// La fenêtre principale une fois le jeu lancé. Trois onglets, rien de plus.
/// Chaque modification est enregistrée immédiatement et appliquée aux fenêtres
/// déjà ouvertes.
/// </summary>
public sealed partial class ConfiguratorViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly GameLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;

    private bool _loading;

    public ConfiguratorViewModel(
        InstanceListViewModel instances,
        SettingsService settings,
        GameLauncher launcher,
        IDialogService dialogs,
        IAppPaths paths)
    {
        Instances = instances;
        _settings = settings;
        _launcher = launcher;
        _dialogs = dialogs;
        _paths = paths;
    }

    public InstanceListViewModel Instances { get; }

    // Onglet Général

    /// <summary>Les neuf positions possibles du bloc de fenêtres.</summary>
    public IReadOnlyList<WindowAnchor> Anchors { get; } = WindowAnchors.All;

    [ObservableProperty]
    private WindowAnchor _gameAnchor = WindowAnchor.MiddleLeft;

    /// <summary>Tailles proposées, proportionnelles à l'écran.</summary>
    public ObservableCollection<SizeChoiceViewModel> Sizes { get; } = [];

    [ObservableProperty]
    private SizeChoiceViewModel? _selectedSize;

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];

    [ObservableProperty]
    private MonitorInfo? _preferredMonitor;

    /// <summary>Vrai s'il y a plus d'un écran : sinon le réglage est inutile.</summary>
    public bool HasSeveralMonitors => Monitors.Count > 1;

    // Onglet Raccourcis, en lecture seule

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];

    // Divers

    public string ProductName => ProductInfo.Name;

    public string Version => ProductInfo.Version;

    /// <summary>Raccourci d'affichage, rappelé en clair dans la fenêtre.</summary>
    [ObservableProperty]
    private string _toggleShortcutText = "Ctrl + P";

    public string Disclaimer =>
        "Projet indépendant, sans lien avec Ankama, Genymobile, Google ni les fabricants d'appareils.";

    /// <summary>Charge l'état des réglages dans la fenêtre.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;

        try
        {
            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            GameAnchor = settings.GameAnchor;

            var presets = await _settings.GetSizePresetsAsync(cancellationToken).ConfigureAwait(true);

            Sizes.Clear();
            foreach (var choice in SizeChoiceViewModel.From(presets))
            {
                Sizes.Add(choice);
            }

            SelectedSize = Sizes.FirstOrDefault(s => s.Index == settings.SizeIndex) ?? Sizes.FirstOrDefault();

            Monitors.Clear();
            foreach (var monitor in _launcher.Monitors)
            {
                Monitors.Add(monitor);
            }

            PreferredMonitor = Monitors.FirstOrDefault(
                m => m.DeviceName == settings.PreferredMonitorDeviceName);

            OnPropertyChanged(nameof(HasSeveralMonitors));

            await RefreshHotkeysAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _loading = false;
        }

        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Relit les raccourcis, après une modification dans l'éditeur.</summary>
    public async Task RefreshHotkeysAsync(CancellationToken cancellationToken = default)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(true);

        Hotkeys.Clear();
        foreach (var binding in hotkeys.Bindings)
        {
            Hotkeys.Add(new HotkeyRowViewModel(binding));
        }

        ToggleShortcutText = hotkeys.For(HotkeyAction.ToggleConfigurator)?.DisplayText ?? "Ctrl + P";
    }

    /// <summary>Rafraîchit ce qui change tout seul : appareils et états.</summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
        Instances.RefreshRunningState();
    }

    [RelayCommand]
    private void SetAnchor(WindowAnchor anchor) => GameAnchor = anchor;

    [RelayCommand]
    private void SetSize(SizeChoiceViewModel? size)
    {
        if (size is not null)
        {
            SelectedSize = size;
        }
    }

    /// <summary>Ouvre l'instance choisie, sans toucher aux autres.</summary>
    [RelayCommand]
    private async Task LaunchInstanceAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var report = await _launcher.LaunchAsync([row.Instance]).ConfigureAwait(true);
        Instances.RefreshRunningState();

        Instances.Problem = report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null;
    }

    /// <summary>Ferme puis rouvre l'instance, jeu compris.</summary>
    [RelayCommand]
    private async Task RestartAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var report = await _launcher.RestartAsync(row.Instance).ConfigureAwait(true);
        Instances.RefreshRunningState();

        Instances.Problem = report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null;
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
    private async Task ArrangeAsync(CancellationToken cancellationToken)
    {
        var moved = await _launcher.ArrangeAsync(cancellationToken).ConfigureAwait(true);

        if (moved == 0)
        {
            Instances.Problem = "Aucune fenêtre de jeu à replacer.";
        }
    }

    [RelayCommand]
    private async Task ForgetDeviceAsync(DeviceGroupViewModel? group)
    {
        if (group is null
            || !_dialogs.Confirm(
                $"Oublier {group.Name} ?\n\nSes instances et leurs réglages seront effacés.",
                "Oublier l'appareil"))
        {
            return;
        }

        await _settings.ForgetDeviceAsync(group.DeviceId).ConfigureAwait(true);
        await Instances.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenLogs() => _dialogs.OpenFolder(_paths.LogsDirectory);

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

    partial void OnSelectedSizeChanged(SizeChoiceViewModel? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        _ = _launcher.ApplySizeAsync(value.Index);
    }

    partial void OnPreferredMonitorChanged(MonitorInfo? value)
    {
        Save(s => s.PreferredMonitorDeviceName = value?.DeviceName);
        _ = _launcher.ArrangeAsync();
    }
}
