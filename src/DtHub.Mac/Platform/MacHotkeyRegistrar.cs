using System.Collections.Concurrent;

using Avalonia.Threading;

using DtHub.Core.Hotkeys;

using Microsoft.Extensions.Logging;

namespace DtHub.Mac.Platform;

/// <summary>
/// The global shortcuts on macOS, the PC's Win32HotkeyRegistrar
/// (src/DtHub.Infrastructure/Hotkeys) in Carbon's terms: RegisterEventHotKey
/// delivers only the declared combinations, needs no permission, and does
/// not repeat a held key, as MOD_NOREPEAT asks on the PC. The keys and
/// modifiers are translated by <see cref="MacKeyMap"/>.
///
/// Carbon wants the main thread, which is Avalonia's UI thread on macOS:
/// every change runs there, one after the other, as the PC runs them on its
/// hotkey thread. The events are raised as on the PC, in order on a thread
/// of their own, so a slow handler holds neither the interface nor macOS.
///
/// The frontmost window is followed by notifications (see MacHotkeys.swift):
/// another application activated, or one of our windows took the keyboard.
/// A poll would have cost a call every quarter of a second for nothing,
/// and still seen a change late.
/// </summary>
internal sealed partial class MacHotkeyRegistrar : IHotkeyRegistrar
{
    private readonly ILogger<MacHotkeyRegistrar> _logger;
    private readonly BlockingCollection<Action> _events = [];
    private readonly Thread _thread;

    // Read and written on the UI thread only.
    private readonly Dictionary<int, HotkeyAction> _registered = [];
    private HotkeySet _hotkeys = HotkeySet.Default;
    private bool _started;
    private int _nextId = 1;

    // Kept so the garbage collector does not release it while macOS holds
    // the pointer.
    private readonly MacKeyboard.NativeEvent _callback;

    private volatile bool _enabled;
    private bool _disposed;

    public MacHotkeyRegistrar(ILogger<MacHotkeyRegistrar> logger)
    {
        _logger = logger;
        _callback = OnNativeEvent;

        _thread = new Thread(RaiseEvents)
        {
            IsBackground = true,
            Name = "DtHub.Hotkeys",
        };

        _thread.Start();
    }

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public event EventHandler<nint>? ForegroundWindowChanged;

    public event EventHandler<nint>? MouseCaptured;

    public bool IsEnabled => _enabled;

    public Task<IReadOnlyList<HotkeyAction>> ApplyAsync(HotkeySet hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        return OnUiAsync(() =>
        {
            _hotkeys = hotkeys;

            if (!_enabled)
            {
                return (IReadOnlyList<HotkeyAction>)[];
            }

            UnregisterAll();
            return RegisterAll();
        });
    }

    public Task SetEnabledAsync(bool enabled) =>
        OnUiAsync(() =>
        {
            if (_enabled == enabled)
            {
                return (IReadOnlyList<HotkeyAction>)[];
            }

            _enabled = enabled;

            if (enabled)
            {
                return RegisterAll();
            }

            UnregisterAll();
            return (IReadOnlyList<HotkeyAction>)[];
        });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Only from the UI thread: once the application is shutting down,
        // nothing else would run it, and quitting the process lets go of
        // the shortcuts anyway.
        if (_started && Dispatcher.UIThread.CheckAccess())
        {
            MacKeyboard.dthub_hotkeys_stop();
            _registered.Clear();
        }

        _events.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(2));
        _events.Dispose();
    }

    /// <summary>Runs on the UI thread, inline when already there.</summary>
    private Task<IReadOnlyList<HotkeyAction>> OnUiAsync(Func<IReadOnlyList<HotkeyAction>> work)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(Started(work));
        }

        return Dispatcher.UIThread.InvokeAsync(() => Started(work)).GetTask();
    }

    private IReadOnlyList<HotkeyAction> Started(Func<IReadOnlyList<HotkeyAction>> work)
    {
        if (_disposed)
        {
            return [];
        }

        if (!_started)
        {
            _started = true;

            if (!MacKeyboard.dthub_hotkeys_start(_callback))
            {
                LogStartFailed();
            }
        }

        return work();
    }

    private List<HotkeyAction> RegisterAll()
    {
        List<HotkeyAction> refused = [];
        var typing = MacKeyboard.TypingKeys();

        foreach (var binding in _hotkeys.Bindings.Where(b => b.IsAssigned))
        {
            var id = _nextId++;

            // A key the Mac does not have (Pause, F21...) is refused as a
            // taken one is: the editor then says so next to it.
            var status = MacKeyMap.ToKeyCode(binding.VirtualKey, typing) is { } code
                ? MacKeyboard.dthub_hotkey_register(id, (uint)code, MacKeyMap.ToCarbonModifiers(binding.Modifiers))
                : NoSuchKey;

            if (status == 0)
            {
                _registered[id] = binding.Action;
            }
            else
            {
                refused.Add(binding.Action);
                LogRefused(HotkeyBinding.DescribeAction(binding.Action), binding.DisplayText, status);
            }
        }

        return refused;
    }

    private void UnregisterAll()
    {
        if (_registered.Count == 0)
        {
            return;
        }

        MacKeyboard.dthub_hotkeys_clear();
        _registered.Clear();
    }

    /// <summary>From macOS, on the main thread: handed to the events' thread.</summary>
    private void OnNativeEvent(int kind, long value)
    {
        Action? raise = kind switch
        {
            MacKeyboard.HotkeyPressed when _registered.TryGetValue((int)value, out var action) =>
                () => HotkeyPressed?.Invoke(this, action),
            MacKeyboard.ForegroundChanged => () => ForegroundWindowChanged?.Invoke(this, (nint)value),
            MacKeyboard.MousePressed => () => MouseCaptured?.Invoke(this, (nint)value),
            _ => null,
        };

        if (raise is null || _disposed)
        {
            return;
        }

        // Never throw back into macOS, even while shutting down (a
        // disposed collection throws ObjectDisposedException, one of these).
        try
        {
            _events.Add(raise);
        }
        catch (InvalidOperationException)
        {
            // Shutting down: nobody is left to tell about this key.
        }
    }

    private void RaiseEvents()
    {
        foreach (var raise in _events.GetConsumingEnumerable())
        {
            // A raw thread: a handler that throws would end the process.
            try
            {
                raise();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Serilog.Log.Error(exception, "{Headline}", DtHub.Core.Localization.Strings.Get("UnexpectedError"));
            }
        }
    }

    /// <summary>Our own status for a key with no Mac counterpart.</summary>
    private const int NoSuchKey = -1;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le raccourci {shortcut} pour « {action} » est refusé par macOS (code {status}) ; un autre logiciel le détient probablement, ou la touche n'existe pas sur Mac.")]
    private partial void LogRefused(string action, string shortcut, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Les raccourcis globaux n'ont pas pu être installés auprès de macOS.")]
    private partial void LogStartFailed();
}
