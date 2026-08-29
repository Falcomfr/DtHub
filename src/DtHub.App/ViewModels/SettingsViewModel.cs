using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Hotkeys;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.App.ViewModels;

/// <summary>
/// Page Paramètres : général, fenêtres, raccourcis, scrcpy, appareils, mises à
/// jour et diagnostic. Chaque modification est enregistrée immédiatement.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly IWindowController _windows;
    private readonly SessionOrchestrator _orchestrator;
    private readonly DiagnosticsService _diagnostics;
    private readonly ThemeManager _theme;
    private readonly IAppPaths _paths;
    private readonly IDialogService _dialogs;

    private HotkeySet _hotkeys = HotkeySet.Default;
    private bool _loading;

    public SettingsViewModel(
        SettingsService settings,
        IWindowController windows,
        SessionOrchestrator orchestrator,
        DiagnosticsService diagnostics,
        ThemeManager theme,
        IAppPaths paths,
        IDialogService dialogs)
    {
        _settings = settings;
        _windows = windows;
        _orchestrator = orchestrator;
        _diagnostics = diagnostics;
        _theme = theme;
        _paths = paths;
        _dialogs = dialogs;
    }

    public override string Title => "Paramètres";

    public override string Subtitle => "Tout est enregistré au fur et à mesure.";

    // Général
    [ObservableProperty]
    private AppTheme _theme_;

    [ObservableProperty]
    private ExitPolicy _exitPolicy;

    [ObservableProperty]
    private bool _reconnectOnStartup;

    [ObservableProperty]
    private bool _checkUpdatesAutomatically;

    // Fenêtres
    [ObservableProperty]
    private int _size1;

    [ObservableProperty]
    private int _size2;

    [ObservableProperty]
    private int _size3;

    [ObservableProperty]
    private int _size4;

    [ObservableProperty]
    private int _defaultSizeIndex;

    [ObservableProperty]
    private MonitorInfo? _preferredMonitor;

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];

    // scrcpy
    [ObservableProperty]
    private int _maxFps;

    [ObservableProperty]
    private int _videoBitrateKbps;

    [ObservableProperty]
    private bool _audioEnabled;

    [ObservableProperty]
    private bool _clipboardSyncEnabled;

    [ObservableProperty]
    private int _virtualDisplayWidth;

    [ObservableProperty]
    private int _virtualDisplayHeight;

    [ObservableProperty]
    private int _virtualDisplayDpi;

    // Applications
    [ObservableProperty]
    private bool _showSystemApps;

    // Raccourcis
    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];

    [ObservableProperty]
    private HotkeyRowViewModel? _capturingRow;

    // Diagnostic
    [ObservableProperty]
    private string? _diagnosticsReport;

    public string LogsDirectory => _paths.LogsDirectory;

    public string DataDirectory => _paths.Root;

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    public IReadOnlyList<ExitPolicy> ExitPolicies { get; } =
        [ExitPolicy.Ask, ExitPolicy.CloseSessions, ExitPolicy.KeepSessions];

    public override async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(LoadAsync, cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        _loading = true;

        try
        {
            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            Theme_ = settings.Theme;
            ExitPolicy = settings.ExitPolicy;
            ReconnectOnStartup = settings.ReconnectOnStartup;
            CheckUpdatesAutomatically = settings.CheckUpdatesAutomatically;

            var percentages = new WindowSizePresets { Percentages = settings.SizePercentages }.Sanitized();
            Size1 = percentages.PercentageAt(0);
            Size2 = percentages.PercentageAt(1);
            Size3 = percentages.PercentageAt(2);
            Size4 = percentages.PercentageAt(3);
            DefaultSizeIndex = settings.DefaultSizeIndex;

            Monitors.Clear();
            foreach (var monitor in _windows.GetMonitors())
            {
                Monitors.Add(monitor);
            }

            PreferredMonitor = Monitors.FirstOrDefault(
                m => m.DeviceName == settings.PreferredMonitorDeviceName);

            MaxFps = settings.MaxFps;
            VideoBitrateKbps = settings.VideoBitrateKbps;
            AudioEnabled = settings.AudioEnabled;
            ClipboardSyncEnabled = settings.ClipboardSyncEnabled;
            VirtualDisplayWidth = settings.VirtualDisplayWidth;
            VirtualDisplayHeight = settings.VirtualDisplayHeight;
            VirtualDisplayDpi = settings.VirtualDisplayDpi;
            ShowSystemApps = settings.ShowSystemApps;

            _hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(true);
            RebuildHotkeyRows();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Démarre la capture d'un nouveau raccourci pour une action.</summary>
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

        RebuildHotkeyRows();

        await _settings.SaveHotkeysAsync(_hotkeys).ConfigureAwait(true);
        await ReportRefusedAsync().ConfigureAwait(true);

        return true;
    }

    [RelayCommand]
    private Task ClearHotkeyAsync(HotkeyRowViewModel? row) => RunAsync(async token =>
    {
        if (row is null)
        {
            return;
        }

        _hotkeys = _hotkeys.Without(row.Action);
        RebuildHotkeyRows();

        await _settings.SaveHotkeysAsync(_hotkeys, token).ConfigureAwait(true);
        await ReportRefusedAsync().ConfigureAwait(true);
    });

    [RelayCommand]
    private Task RestoreDefaultHotkeysAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        _hotkeys = HotkeySet.Default;
        RebuildHotkeyRows();

        await _settings.SaveHotkeysAsync(_hotkeys, token).ConfigureAwait(true);
        await ReportRefusedAsync().ConfigureAwait(true);
    }, cancellationToken);

    [RelayCommand]
    private Task BuildDiagnosticsAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        DiagnosticsReport = await _diagnostics.BuildReportAsync(token).ConfigureAwait(true);
    }, cancellationToken);

    [RelayCommand]
    private void CopyDiagnostics()
    {
        if (!string.IsNullOrWhiteSpace(DiagnosticsReport))
        {
            _dialogs.CopyToClipboard(DiagnosticsReport);
        }
    }

    [RelayCommand]
    private void OpenLogsFolder() => _dialogs.OpenFolder(_paths.LogsDirectory);

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenFolder(_paths.Root);

    private void RebuildHotkeyRows()
    {
        Hotkeys.Clear();
        foreach (var binding in _hotkeys.Bindings)
        {
            Hotkeys.Add(new HotkeyRowViewModel(binding));
        }
    }

    /// <summary>
    /// Signale les raccourcis que Windows a refusés, généralement parce qu'un
    /// autre logiciel les détient déjà.
    /// </summary>
    private async Task ReportRefusedAsync()
    {
        var refused = await _orchestrator.ReloadHotkeysAsync().ConfigureAwait(true);

        StatusMessage = refused.Count == 0
            ? null
            : "Refusé par Windows, probablement pris par un autre logiciel : "
              + string.Join(", ", refused.Select(HotkeyBinding.DescribeAction));
    }

    private static string Describe(HotkeyValidationResult result, HotkeyAction? conflict) => result switch
    {
        HotkeyValidationResult.NoKey => "Aucune touche saisie.",
        HotkeyValidationResult.ModifierOnly => "Choisissez une touche en plus du modificateur.",
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

    partial void OnTheme_Changed(AppTheme value)
    {
        Save(s => s.Theme = value);
        _theme.Apply(value);
    }

    partial void OnExitPolicyChanged(ExitPolicy value) => Save(s => s.ExitPolicy = value);

    partial void OnReconnectOnStartupChanged(bool value) => Save(s => s.ReconnectOnStartup = value);

    partial void OnCheckUpdatesAutomaticallyChanged(bool value) => Save(s => s.CheckUpdatesAutomatically = value);

    partial void OnDefaultSizeIndexChanged(int value) => Save(s => s.DefaultSizeIndex = value);

    partial void OnSize1Changed(int value) => SaveSizes();

    partial void OnSize2Changed(int value) => SaveSizes();

    partial void OnSize3Changed(int value) => SaveSizes();

    partial void OnSize4Changed(int value) => SaveSizes();

    private void SaveSizes() => Save(s => s.SizePercentages = [Size1, Size2, Size3, Size4]);

    partial void OnPreferredMonitorChanged(MonitorInfo? value) =>
        Save(s => s.PreferredMonitorDeviceName = value?.DeviceName);

    partial void OnMaxFpsChanged(int value) => Save(s => s.MaxFps = value);

    partial void OnVideoBitrateKbpsChanged(int value) => Save(s => s.VideoBitrateKbps = value);

    partial void OnAudioEnabledChanged(bool value) => Save(s => s.AudioEnabled = value);

    partial void OnClipboardSyncEnabledChanged(bool value) => Save(s => s.ClipboardSyncEnabled = value);

    partial void OnVirtualDisplayWidthChanged(int value) => Save(s => s.VirtualDisplayWidth = value);

    partial void OnVirtualDisplayHeightChanged(int value) => Save(s => s.VirtualDisplayHeight = value);

    partial void OnVirtualDisplayDpiChanged(int value) => Save(s => s.VirtualDisplayDpi = value);

    partial void OnShowSystemAppsChanged(bool value) => Save(s => s.ShowSystemApps = value);
}
