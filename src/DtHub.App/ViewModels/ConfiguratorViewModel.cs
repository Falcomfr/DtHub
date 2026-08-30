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

        // Les raccourcis écrivent la taille et l'ancrage sans passer par ici :
        // sans cet abonnement, le curseur et la grille gardaient la valeur
        // qu'ils avaient à l'ouverture et mentaient jusqu'au redémarrage.
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>
    /// Reflète une écriture venue d'ailleurs, raccourci clavier compris.
    ///
    /// Le garde-fou de chargement est indispensable : sans lui, le curseur qui
    /// se remet à la bonne valeur redéclencherait un redimensionnement, et la
    /// grille un replacement, chacun réécrivant les réglages en boucle.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettingsDocument document)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        dispatcher.InvokeAsync(async () =>
        {
            if (_loading || _movingWindows)
            {
                return;
            }

            var presets = await _settings.GetSizePresetsAsync().ConfigureAwait(true);

            _loading = true;

            try
            {
                GameAnchor = document.GameAnchor;
                Quality = document.Quality;
                Zoom = document.GameZoom;
                SizePercent = document.CustomSizePercent > 0
                    ? document.CustomSizePercent
                    : presets.PercentageAt(document.SizeIndex);
            }
            finally
            {
                _loading = false;
            }
        });
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
    /// Compromis entre finesse de l'image et charge de la machine. L'image ne
    /// change qu'à la réouverture des fenêtres : les options sont figées au
    /// lancement de scrcpy.
    /// </summary>
    [ObservableProperty]
    private StreamQuality _quality = StreamQuality.Medium;

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

    /// <summary>Raccourci du côte à côte, affiché à côté du bouton.</summary>
    [ObservableProperty]
    private string _tileShortcutText = string.Empty;

    /// <summary>Raccourci de sortie, affiché sous le bouton Quitter.</summary>
    [ObservableProperty]
    private string _quitShortcutText = string.Empty;

    /// <summary>Distance apparente dans le jeu, figée à l'ouverture d'une session.</summary>
    [ObservableProperty]
    private GameZoom _zoom = GameZoom.Normal;

    /// <summary>Vrai le temps que les fenêtres se referment et rouvrent.</summary>
    [ObservableProperty]
    private bool _isReopening;

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

            Quality = settings.Quality;
            Zoom = settings.GameZoom;

            SizePercent = settings.CustomSizePercent > 0
                ? settings.CustomSizePercent
                : presets.PercentageAt(settings.SizeIndex);

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
        TileShortcutText = hotkeys.For(HotkeyAction.Tile)?.DisplayText ?? string.Empty;
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

    /// <summary>Range les fenêtres côte à côte, l'active à droite.</summary>
    [RelayCommand]
    private async Task TileAsync()
    {
        var placed = await _launcher.TileAsync().ConfigureAwait(true);

        if (placed == 0)
        {
            Instances.Problem = "Aucune fenêtre de jeu à ranger.";
        }
    }

    [RelayCommand]
    private void OpenLogs() => _dialogs.OpenFolder(_paths.LogsDirectory);

    private Task SaveAsync(Action<AppSettingsDocument> mutate) =>
        _loading ? Task.CompletedTask : _settings.UpdateAsync(mutate);

    partial void OnGameAnchorChanged(WindowAnchor value)
    {
        // Le replacement suit le garde-fou de chargement, comme
        // l'enregistrement : sans cela, relire les réglages au démarrage
        // replacerait toutes les fenêtres et effacerait leur géométrie
        // mémorisée avant même que l'utilisateur ait touché à quoi que ce soit.
        if (_loading)
        {
            return;
        }

        _ = ApplyAnchorAsync(value);
    }

    /// <summary>
    /// Enregistre le coin choisi, puis replace les fenêtres.
    ///
    /// L'écriture est attendue : le replacement relit les réglages pour en
    /// tirer le coin, et lancer les deux de front faisait relire l'ancienne
    /// valeur. Cliquer une case ne faisait alors rien de visible.
    /// </summary>
    private async Task ApplyAnchorAsync(WindowAnchor value)
    {
        await SaveAsync(s => s.GameAnchor = value).ConfigureAwait(true);
        await _launcher.ArrangeAsync().ConfigureAwait(true);
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

    partial void OnQualityChanged(StreamQuality value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetQualityAsync(value));
    }

    partial void OnZoomChanged(GameZoom value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetZoomAsync(value));
    }

    /// <summary>
    /// Enregistre un réglage qui n'agit qu'à l'ouverture d'une session, puis
    /// rouvre les fenêtres pour qu'il se voie.
    ///
    /// Sans cela, changer la qualité ou la distance ne montrait rien : ce sont
    /// des arguments de démarrage de scrcpy, figés pour toute la session. Le
    /// choix de rouvrir plutôt que d'attendre la prochaine fois est celui de
    /// l'utilisateur, qui ne comprenait pas pourquoi le réglage semblait mort.
    /// </summary>
    private async Task ApplyStartupSettingAsync(Func<Task> write)
    {
        if (IsReopening)
        {
            return;
        }

        IsReopening = true;

        try
        {
            await write().ConfigureAwait(true);

            var report = await _launcher.ReopenAsync().ConfigureAwait(true);

            if (report.Problems.Count > 0)
            {
                _dialogs.ShowWarning(string.Join(Environment.NewLine, report.Problems));
            }
        }
        finally
        {
            IsReopening = false;
        }
    }


}
