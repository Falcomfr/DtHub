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
    private bool _movingWindows;
    private int? _pendingPercent;
    private CancellationTokenSource? _persistSize;

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

    /// <summary>
    /// Taille des fenêtres, en pourcentage de la zone utilisable de l'écran.
    /// Elle est donc proportionnelle à l'écran employé.
    /// </summary>
    [ObservableProperty]
    private int _sizePercent = 60;

    /// <summary>
    /// Vrai pour laisser la largeur libre, faux pour verrouiller le rapport.
    /// Les deux remplissent toujours la fenêtre et ne rechargent jamais.
    /// </summary>
    [ObservableProperty]
    private bool _freeWidthResize = true;

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

    /// <summary>
    /// Raccourci du replacement, affiché à côté du bouton. Vide quand aucun
    /// raccourci n'est associé à l'action.
    /// </summary>
    [ObservableProperty]
    private string _rearrangeShortcutText = string.Empty;

    /// <summary>Raccourci de sortie, affiché sous le bouton Quitter.</summary>
    [ObservableProperty]
    private string _quitShortcutText = string.Empty;

    public string Disclaimer =>
        "Projet indépendant, sans lien avec Ankama.";

    /// <summary>Charge l'état des réglages dans la fenêtre.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;

        try
        {
            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            GameAnchor = settings.GameAnchor;

            var presets = await _settings.GetSizePresetsAsync(cancellationToken).ConfigureAwait(true);

            SizePercent = settings.CustomSizePercent > 0
                ? settings.CustomSizePercent
                : presets.PercentageAt(settings.SizeIndex);

            FreeWidthResize = settings.FreeWidthResize;

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

        // Les actions de la barre du bas rappellent leur raccourci, et le
        // suivent quand il est modifié dans l'éditeur.
        RearrangeShortcutText = hotkeys.For(HotkeyAction.Rearrange)?.DisplayText ?? string.Empty;
        QuitShortcutText = hotkeys.For(HotkeyAction.Quit)?.DisplayText ?? string.Empty;

    }

    /// <summary>Rafraîchit ce qui change tout seul : appareils et états.</summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
        Instances.RefreshRunningState();
    }

    [RelayCommand]
    private void SetAnchor(WindowAnchor anchor) => GameAnchor = anchor;

    /// <summary>
    /// Empile les fenêtres sur celle qui est active, ou sur la première.
    /// </summary>
    [RelayCommand]
    private async Task RearrangeAsync() => await _launcher.StackOnActiveAsync().ConfigureAwait(true);

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

    partial void OnSizePercentChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        _ = FollowSliderAsync(value);
        _ = PersistSizeSoonAsync(value);
    }

    /// <summary>
    /// Suit le curseur au plus près. Un seul déplacement à la fois : les crans
    /// arrivent plus vite que les fenêtres ne bougent, et les empiler ferait
    /// traîner la taille derrière le curseur. Seule la dernière valeur reçue
    /// pendant un déplacement est appliquée ensuite.
    /// </summary>
    private async Task FollowSliderAsync(int percent)
    {
        if (_movingWindows)
        {
            _pendingPercent = percent;
            return;
        }

        _movingWindows = true;

        try
        {
            var next = percent;

            while (true)
            {
                await _launcher.ApplyPercentAsync(next, persist: false).ConfigureAwait(true);

                if (_pendingPercent is not { } queued)
                {
                    break;
                }

                _pendingPercent = null;
                next = queued;
            }
        }
        finally
        {
            _movingWindows = false;
        }
    }

    /// <summary>
    /// Écrit la taille une fois le curseur reposé. Chaque cran déclencherait
    /// sinon une réécriture complète du fichier de réglages.
    /// </summary>
    private async Task PersistSizeSoonAsync(int percent)
    {
        _persistSize?.Cancel();
        _persistSize?.Dispose();

        var cancellation = new CancellationTokenSource();
        _persistSize = cancellation;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellation.Token).ConfigureAwait(true);

            await _launcher.ApplyPercentAsync(percent, persist: true, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Le curseur a bougé de nouveau : c'est la valeur suivante qui compte.
        }
    }

    partial void OnFreeWidthResizeChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // Le mode est fixé à l'ouverture de chaque fenêtre : le changer
        // demande de les rouvrir, ce que l'utilisateur vient de demander
        // explicitement.
        _ = ApplyResizeModeAsync(value);
    }

    private async Task ApplyResizeModeAsync(bool free)
    {
        await _settings.SetFreeWidthResizeAsync(free).ConfigureAwait(true);
        await _launcher.ReopenAllAsync().ConfigureAwait(true);
    }

    /// <summary>Choisit la largeur libre.</summary>
    [RelayCommand]
    private void UseFreeWidth() => FreeWidthResize = true;

    /// <summary>Choisit le rapport verrouillé.</summary>
    [RelayCommand]
    private void UseLockedAspect() => FreeWidthResize = false;

    partial void OnPreferredMonitorChanged(MonitorInfo? value)
    {
        Save(s => s.PreferredMonitorDeviceName = value?.DeviceName);
        _ = _launcher.ArrangeAsync();
    }
}
