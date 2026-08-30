using DtHub.Core.Windows;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Bureau simulé : des écrans, des fenêtres, et la trace de ce qu'on leur a
/// fait. Permet de vérifier l'empilement sans manipuler de vraies fenêtres.
/// </summary>
public sealed class FakeWindowController : IWindowController
{
    private readonly List<WindowHandleInfo> _windows = [];
    private readonly Dictionary<nint, ScreenRect> _rects = [];

    public FakeWindowController(params MonitorInfo[] monitors) =>
        Monitors = monitors.Length > 0 ? [.. monitors] : [PrimaryMonitor];

    /// <summary>Écran 1920x1080 avec barre des tâches, cas le plus courant.</summary>
    public static readonly MonitorInfo PrimaryMonitor = new()
    {
        DeviceName = @"\\.\DISPLAY1",
        Bounds = new ScreenRect(0, 0, 1920, 1080),
        WorkArea = new ScreenRect(0, 0, 1920, 1040),
        IsPrimary = true,
    };

    public List<MonitorInfo> Monitors { get; }

    /// <summary>Fenêtre actuellement au premier plan.</summary>
    public nint Foreground { get; set; }

    /// <summary>Fenêtres passées en mode sans bordure.</summary>
    public HashSet<nint> Borderless { get; } = [];

    /// <summary>Ordre des appels au focus, pour vérifier le parcours.</summary>
    public List<nint> FocusCalls { get; } = [];

    /// <summary>Déclare une fenêtre appartenant à un processus.</summary>
    public FakeWindowController AddWindow(nint handle, int processId, string title)
    {
        _windows.Add(new WindowHandleInfo(handle, title, processId));
        _rects[handle] = new ScreenRect(0, 0, 400, 400);
        return this;
    }

    /// <summary>Fait disparaître une fenêtre, comme à la fermeture d'une session.</summary>
    public void RemoveWindow(nint handle)
    {
        _windows.RemoveAll(w => w.Handle == handle);
        _rects.Remove(handle);
    }

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

    public IReadOnlyList<WindowHandleInfo> FindWindows(int processId) =>
        [.. _windows.Where(w => w.ProcessId == processId)];

    public bool IsWindow(nint handle) => _windows.Exists(w => w.Handle == handle);

    /// <summary>Encombrement simulé de la barre de titre et des bordures.</summary>
    public (int Width, int Height) Chrome { get; set; }

    public (int Width, int Height) GetWindowChrome(string? monitorDeviceName) => Chrome;

    public ScreenRect? GetWindowRect(nint handle) =>
        _rects.TryGetValue(handle, out var rect) ? rect : null;

    public ScreenRect? GetClientRect(nint handle) =>
        _rects.TryGetValue(handle, out var rect)
            ? new ScreenRect(0, 0, rect.Width - Chrome.Width, rect.Height - Chrome.Height)
            : null;

    /// <summary>Rectangles posés, dans l'ordre, pour vérifier les remises en page.</summary>
    public List<(nint Handle, ScreenRect Rect)> Moves { get; } = [];

    public void MoveWindow(nint handle, ScreenRect rect, bool bringToFront = false)
    {
        if (IsWindow(handle))
        {
            _rects[handle] = rect;
            Moves.Add((handle, rect));
        }
    }

    /// <summary>Titres réécrits, pour vérifier le rappel du raccourci.</summary>
    public Dictionary<nint, string> Titles { get; } = [];

    public void SetTitle(nint handle, string title) => Titles[handle] = title;

    public void Focus(nint handle)
    {
        FocusCalls.Add(handle);
        Foreground = handle;
    }

    /// <summary>Ordre d'empilement, du plus récent remonté au plus ancien.</summary>
    public List<nint> RaiseCalls { get; } = [];

    public void Raise(nint handle) => RaiseCalls.Add(handle);

    public void SetBorderless(nint handle, bool borderless)
    {
        if (borderless)
        {
            Borderless.Add(handle);
        }
        else
        {
            Borderless.Remove(handle);
        }
    }

    public nint GetForegroundWindow() => Foreground;
}
