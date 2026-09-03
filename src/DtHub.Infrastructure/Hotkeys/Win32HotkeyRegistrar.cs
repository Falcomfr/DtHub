using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using DtHub.Core.Hotkeys;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Hotkeys;

/// <summary>
/// Enregistre les raccourcis avec <c>RegisterHotKey</c>, sur un fil dédié
/// muni de sa propre boucle de messages.
///
/// Le choix de <c>RegisterHotKey</c> plutôt que d'un crochet clavier de bas
/// niveau est délibéré : un tel crochet verrait toutes les frappes du système,
/// ce qui serait disproportionné pour le besoin et impossible à distinguer
/// d'un enregistreur de frappe. Ici, seules les combinaisons déclarées sont
/// interceptées, et uniquement pendant qu'une fenêtre de DT Hub est active.
/// </summary>
public sealed partial class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly Dictionary<int, HotkeyAction> _registered = [];
    private readonly ILogger<Win32HotkeyRegistrar> _logger;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;

    private HotkeySet _hotkeys = HotkeySet.Default;
    private uint _threadId;
    private nint _winEventHook;
    private int _nextId = 1;
    private bool _enabled;
    private bool _disposed;

    public Win32HotkeyRegistrar(ILogger<Win32HotkeyRegistrar> logger)
    {
        _logger = logger;

        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "DtHub.Hotkeys",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public event EventHandler<nint>? ForegroundWindowChanged;

    public bool IsEnabled => _enabled;

    public async Task<IReadOnlyList<HotkeyAction>> ApplyAsync(HotkeySet hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        await _ready.Task.ConfigureAwait(false);

        _hotkeys = hotkeys;

        if (!_enabled)
        {
            return [];
        }

        return await PostAsync(() =>
        {
            UnregisterAllCore();
            return RegisterAllCore();
        }).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        await _ready.Task.ConfigureAwait(false);

        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;

        await PostAsync(() =>
        {
            if (enabled)
            {
                return RegisterAllCore();
            }

            UnregisterAllCore();
            return (IReadOnlyList<HotkeyAction>)[];
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_threadId != 0)
        {
            Post(() =>
            {
                UnregisterAllCore();

                if (_winEventHook != 0)
                {
                    _ = UnhookWinEvent(_winEventHook);
                    _winEventHook = 0;
                }
            });

            _ = PostThreadMessage(_threadId, WmQuit, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>Boucle de messages du fil dédié aux raccourcis.</summary>
    private void RunMessageLoop()
    {
        _threadId = GetCurrentThreadId();

        // Force la création de la file de messages avant tout envoi.
        _ = PeekMessage(out _, 0, 0, 0, PmNoRemove);

        // Le crochet d'événement système sert uniquement à savoir quelle
        // fenêtre est au premier plan : aucune frappe n'y transite.
        _foregroundCallback = OnForegroundChanged;
        _winEventHook = SetWinEventHook(
            EventSystemForeground, EventSystemForeground, 0, _foregroundCallback, 0, 0, WinEventOutOfContext);

        _ready.TrySetResult();

        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.message == WmHotkey)
            {
                var id = (int)message.wParam;

                if (_registered.TryGetValue(id, out var action))
                {
                    HotkeyPressed?.Invoke(this, action);
                }

                continue;
            }

            if (message.message == WmRunPending)
            {
                while (_pending.TryDequeue(out var work))
                {
                    work();
                }

                continue;
            }

            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
    }

    private void OnForegroundChanged(
        nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time) =>
        ForegroundWindowChanged?.Invoke(this, window);

    private List<HotkeyAction> RegisterAllCore()
    {
        List<HotkeyAction> refused = [];

        foreach (var binding in _hotkeys.Bindings.Where(b => b.IsAssigned))
        {
            var id = _nextId++;

            // MOD_NOREPEAT évite qu'une touche maintenue déclenche l'action
            // des dizaines de fois par seconde.
            var modifiers = ToNativeModifiers(binding.Modifiers) | ModNoRepeat;

            if (RegisterHotKey(0, id, modifiers, (uint)binding.VirtualKey))
            {
                _registered[id] = binding.Action;
            }
            else
            {
                refused.Add(binding.Action);
                LogRefused(HotkeyBinding.DescribeAction(binding.Action), binding.DisplayText);
            }
        }

        return refused;
    }

    private void UnregisterAllCore()
    {
        foreach (var id in _registered.Keys)
        {
            _ = UnregisterHotKey(0, id);
        }

        _registered.Clear();
    }

    private void Post(Action work)
    {
        _pending.Enqueue(work);
        _ = PostThreadMessage(_threadId, WmRunPending, 0, 0);
    }

    private Task<IReadOnlyList<HotkeyAction>> PostAsync(Func<IReadOnlyList<HotkeyAction>> work)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<HotkeyAction>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Post(() =>
        {
            try
            {
                completion.TrySetResult(work());
            }
            catch (Exception exception)
            {
                // Sans filtre, et c'est voulu : ce bloc ne traite pas la faute,
                // il la fait voyager du fil des raccourcis vers celui qui
                // attend. La borner ici la ferait disparaître au lieu d'arriver
                // à qui peut la traiter.
                completion.TrySetException(exception);
            }
        });

        return completion.Task;
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint native = 0;

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            native |= 0x0001;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            native |= 0x0002;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            native |= 0x0004;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            native |= 0x0008;
        }

        return native;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le raccourci {shortcut} pour « {action} » est refusé par Windows ; un autre logiciel le détient probablement.")]
    private partial void LogRefused(string action, string shortcut);

    // Le délégué est conservé pour que le ramasse-miettes ne le libère pas
    // pendant que Windows détient le pointeur.
    private WinEventProc? _foregroundCallback;

    private const uint WmQuit = 0x0012;
    private const uint WmHotkey = 0x0312;
    private const uint WmRunPending = 0x0400 + 1;
    private const uint ModNoRepeat = 0x4000;
    private const uint PmNoRemove = 0x0000;
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    private delegate void WinEventProc(
        nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out NativeMessage message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}
