using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using DtHub.Core;
using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Windows;

namespace DtHub.Mac;

/// <summary>
/// The one window of the Mac version: the accounts found on the plugged
/// phones, each with a button to open or close it. Windows are laid out
/// in the same four-cell grid as the Windows version's.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly DeviceDiscoveryService _discovery;
    private readonly DofusInstanceService _instances;
    private readonly ScrcpySessionManager _sessions;
    private readonly SettingsService _settings;

    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _rows = new() { Spacing = 6 };

    private IReadOnlyList<AndroidDevice> _devices = [];
    private IReadOnlyList<DofusInstance> _found = [];

    public MainWindow(
        DeviceDiscoveryService discovery,
        DofusInstanceService instances,
        ScrcpySessionManager sessions,
        SettingsService settings)
    {
        _discovery = discovery;
        _instances = instances;
        _sessions = sessions;
        _settings = settings;

        Title = ProductInfo.Name;
        Width = 440;
        Height = 380;

        var refresh = new Button { Content = Strings.Get("MacRefresh") };
        refresh.Click += async (_, _) => await RefreshAsync();

        var openAll = new Button { Content = Strings.Get("MacOpenAll") };
        openAll.Click += async (_, _) => await OpenAllAsync();

        var closeAll = new Button { Content = Strings.Get("MacCloseAll") };
        closeAll.Click += async (_, _) => await _sessions.StopAllAsync();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toolbar.Children.AddRange([refresh, openAll, closeAll]);

        var layout = new DockPanel { Margin = new Avalonia.Thickness(16) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        _status.Margin = new Avalonia.Thickness(0, 12);
        layout.Children.AddRange([toolbar, _status, new ScrollViewer { Content = _rows }]);
        Content = layout;

        // A session also ends when its scrcpy window is closed by hand.
        _sessions.SessionChanged += (_, _) => Dispatcher.UIThread.Post(ShowRows);

        Opened += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _status.Text = Strings.Get("MacSearching");

        try
        {
            var result = await _discovery.RefreshAsync(CancellationToken.None);
            _devices = [.. result.Connected];
            _found = await _instances.DiscoverAsync(_devices);

            _status.Text = _found.Count == 0
                ? Strings.Get("MacNoGame")
                : Strings.Format("MacFound", _found.Count);
        }
        catch (AdbException exception)
        {
            // Shown to the player: adb missing or not answering.
            _status.Text = exception.UserMessage;
        }

        ShowRows();
    }

    private void ShowRows()
    {
        _rows.Children.Clear();

        foreach (var instance in _found)
        {
            var session = _sessions.ActiveSessions.FirstOrDefault(
                s => string.Equals(s.Target.Key, instance.Key, StringComparison.Ordinal));

            var button = new Button { Content = Strings.Get(session is null ? "MacOpen" : "MacClose") };
            button.Click += async (_, _) =>
            {
                if (session is null)
                {
                    await OpenAsync(instance);
                }
                else
                {
                    await _sessions.StopAsync(session.Id);
                }
            };

            var row = new DockPanel();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.AddRange(
            [
                button,
                new TextBlock
                {
                    Text = $"{instance.DeviceName} · {instance.DisplayName}",
                    VerticalAlignment = VerticalAlignment.Center,
                },
            ]);
            _rows.Children.Add(row);
        }
    }

    private async Task OpenAllAsync()
    {
        // One after the other: the session manager already serializes
        // openings per phone, and the grid cell follows the open count.
        foreach (var instance in _found.ToList())
        {
            if (!_sessions.ActiveSessions.Any(s => string.Equals(s.Target.Key, instance.Key, StringComparison.Ordinal)))
            {
                await OpenAsync(instance);
            }
        }
    }

    private async Task OpenAsync(DofusInstance instance)
    {
        var device = _devices.FirstOrDefault(d => string.Equals(d.Id, instance.DeviceId, StringComparison.Ordinal));
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;

        if (device is null || screen is null)
        {
            return;
        }

        // scrcpy places its window in points on macOS, while Avalonia
        // gives the screen in pixels.
        // shortcut: no title bar allowance and no Retina check yet, calibrate on the Mac (docs/macos.md).
        var scale = screen.Scaling;
        var area = screen.WorkingArea;
        var work = new ScreenRect(
            (int)(area.X / scale), (int)(area.Y / scale), (int)(area.Width / scale), (int)(area.Height / scale));
        var aspect = (double)screen.Bounds.Width / screen.Bounds.Height;
        var cell = TileLayout.Quarter(work, _sessions.ActiveSessions.Count, aspect, (0, 0));

        // Same sizing as the Windows launcher's WithDisplayFor: the game
        // renders at the window's size in pixels, capped by the quality tier.
        var settings = await _settings.GetAsync();
        var quality = QualityProfile.For(settings.Quality, settings.CustomQuality);
        var (width, height) = DisplayLadder.For(
            (int)(cell.Height * scale), screen.Bounds.Width, screen.Bounds.Height,
            quality.MaximumDisplayHeight, settings.SharpImage,
            windowAspect: cell.Width / (double)cell.Height);

        var options = await _settings.GetScrcpyOptionsAsync() with
        {
            VirtualDisplayWidth = width,
            VirtualDisplayHeight = height,
            VirtualDisplayDpi = ZoomProfile.DpiFor(height, settings.GameZoom, quality.LayoutHeight),
            VideoBitrateKbps = quality.BitrateFor(width, height),
            MaxFps = quality.MaxFps,
            VideoCodec = quality.VideoCodec,
            DeviceSdkVersion = device.SdkVersion,
        };

        var target = new LaunchTarget
        {
            DeviceId = instance.DeviceId,
            Serial = device.Serial,
            UserId = instance.UserId,
            PackageName = instance.PackageName,
            LaunchComponent = instance.LaunchComponent,
            DisplayName = instance.DisplayName,
        };

        var session = await _sessions.StartAsync(
            target, options, new ScrcpyWindowPlacement(cell.X, cell.Y, cell.Width, cell.Height));

        if (session.FailureMessage is { } failure)
        {
            _status.Text = failure;
        }

        ShowRows();
    }
}
