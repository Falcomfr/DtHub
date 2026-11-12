using DtHub.App.Windows;
using DtHub.Core.IPhone;
using DtHub.Core.Windows;
using DtHub.Infrastructure.IPhone;

namespace DtHub.App.Services;

/// <summary>
/// An iOS account's window, for the window manager. Opened again after an
/// unplug or a failure, it gets a new <see cref="Window"/> under the same key.
/// </summary>
public sealed class IosGameWindow(string key, IPhoneDevice device, string? bundleId) : IGameWindow
{
    private IPhoneMirrorWindow? _window;

    /// <summary>A fresh one when a given-up window's device comes back.</summary>
    public IosWindowLife Life { get; internal set; } = new();

    /// <summary>As last listed: a device back from an unplug is listed afresh.</summary>
    public IPhoneDevice Device { get; internal set; } = device;

    public IPhoneMirrorWindow Window
    {
        get => _window ?? throw new InvalidOperationException("Not shown yet.");
        internal set
        {
            _window = value;

            // Kept, not read from the window: WPF only answers on its own thread.
            WindowHandle = value.Handle;
        }
    }

    /// <summary>The game on the device, through the current stream.</summary>
    public IosGameControl? Game { get; internal set; }

    /// <summary>DOFUS Touch on the device, once found.</summary>
    public string? BundleId { get; internal set; } = bundleId;

    /// <summary>The device was asked for DOFUS Touch and does not have it.</summary>
    internal bool GameMissing { get; set; }

    /// <summary>Held in the tabbed frame: a window opened again for it is born for the frame.</summary>
    internal bool Docked { get; set; }

    /// <summary>The stream served at least once: only then is a failure tried again, as Android's.</summary>
    internal bool Served { get; set; }

    /// <summary>When the current stream started with the HD picture, null without it (<see cref="IosHdGrace"/>).</summary>
    internal DateTime? HdStartedUtc { get; set; }

    /// <summary>For Android's quiet spell after which the attempts start over.</summary>
    internal DateTime LastFailure { get; set; }

    public string Id => "ios:" + Device.Udid;

    public string Key => key;

    public nint WindowHandle { get; private set; }

    public bool IsAlive => Life.State is IosWindowState.Open or IosWindowState.Unplugged or IosWindowState.Opening;

    public double SourceAspectRatio => _window?.Aspect ?? 0;

    public string WindowTitle => Device.Name;

    public int ProcessId => Environment.ProcessId;
}
