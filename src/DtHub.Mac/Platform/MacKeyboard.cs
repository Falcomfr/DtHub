using System.Runtime.InteropServices;

namespace DtHub.Mac.Platform;

/// <summary>
/// The calls into Native/MacHotkeys.swift (libDtHubMac.dylib): the global
/// shortcuts, the watch on the frontmost window, and the keyboard layout.
/// All of them on the main thread, Avalonia's UI thread on macOS.
/// </summary>
internal static class MacKeyboard
{
    private const string Library = "libDtHubMac";

    /// <summary>What the native side reports, see <see cref="NativeEvent"/>.</summary>
    public const int HotkeyPressed = 1;

    public const int ForegroundChanged = 2;

    public const int MousePressed = 3;

    /// <summary>
    /// A native event: <paramref name="kind"/> says which, and
    /// <paramref name="value"/> carries the shortcut's id or a CGWindowID.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void NativeEvent(int kind, long value);

    /// <summary>
    /// The key code typing each character with no modifier on the current
    /// layout. Read again on each call: the player may switch layouts.
    /// </summary>
    public static Func<char, int?> TypingKeys()
    {
        var keys = new Dictionary<char, int>();

        // The main block only: the keypad's codes (0x41 to 0x5C) type the
        // same signs as some of its keys, and would take their place.
        for (var code = 0; code < 0x41; code++)
        {
            if (CharacterOf(code) is { } typed)
            {
                keys.TryAdd(typed, code);
            }
        }

        return letter => keys.TryGetValue(letter, out var code) ? code : null;
    }

    /// <summary>The character a key types with no modifier, or <c>null</c>.</summary>
    public static char? CharacterOf(int keyCode)
    {
        var typed = dthub_key_character((ushort)keyCode);
        return typed == 0 ? null : (char)typed;
    }

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool dthub_hotkeys_start(NativeEvent onEvent);

    [DllImport(Library)]
    public static extern int dthub_hotkey_register(int id, uint keyCode, uint modifiers);

    [DllImport(Library)]
    public static extern void dthub_hotkeys_clear();

    [DllImport(Library)]
    public static extern void dthub_hotkeys_stop();

    [DllImport(Library)]
    private static extern ushort dthub_key_character(ushort keyCode);
}
