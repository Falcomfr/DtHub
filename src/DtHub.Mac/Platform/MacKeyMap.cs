using DtHub.Core.Hotkeys;

namespace DtHub.Mac.Platform;

/// <summary>
/// The PC's key model (Windows virtual-key codes, Ctrl / Alt / Shift / Win)
/// in macOS terms: Carbon key codes, which name a place on the keyboard,
/// and Carbon modifier flags. Pure, so the tests can link it.
///
/// The modifiers: Ctrl is the Mac's Control, Alt its Option, Shift its
/// Shift, and the Windows key its Command, the key in the same place. The
/// PC's defaults (Ctrl + P, Ctrl + Tab...) therefore keep their Control
/// key: Command + Tab and Command + P belong to macOS and every program.
///
/// The letters follow the keyboard layout, as on the PC, where a virtual
/// key is the letter printed on the key: the Mac's codes are the places of
/// an American keyboard, and "A" is where an AZERTY keyboard prints "Q".
/// A letter, and a punctuation sign Windows names a key after, is therefore
/// looked up in the current layout, through the functions passed in.
/// Everything else (digits, function keys, arrows) goes by place, as
/// Windows itself numbers the digit row of an AZERTY keyboard.
/// </summary>
internal static class MacKeyMap
{
    // Carbon's modifier flags (Events.h).
    public const uint CmdKey = 0x0100;
    public const uint ShiftKey = 0x0200;
    public const uint OptionKey = 0x0800;
    public const uint ControlKey = 0x1000;

    /// <summary>Windows virtual key to macOS key code, by place.</summary>
    private static readonly Dictionary<int, int> ByPlace = new()
    {
        [VirtualKeys.Back] = 0x33,
        [VirtualKeys.Tab] = 0x30,
        [VirtualKeys.Enter] = 0x24,
        [VirtualKeys.CapsLock] = 0x39,
        [VirtualKeys.Escape] = 0x35,
        [VirtualKeys.Space] = 0x31,
        [VirtualKeys.PageUp] = 0x74,
        [VirtualKeys.PageDown] = 0x79,
        [VirtualKeys.End] = 0x77,
        [VirtualKeys.Home] = 0x73,
        [VirtualKeys.Left] = 0x7B,
        [VirtualKeys.Up] = 0x7E,
        [VirtualKeys.Right] = 0x7C,
        [VirtualKeys.Down] = 0x7D,

        // The Mac's Help key stands where Insert does on a PC keyboard.
        [VirtualKeys.Insert] = 0x72,
        [VirtualKeys.Delete] = 0x75,

        [0x30] = 0x1D,
        [0x31] = 0x12,
        [0x32] = 0x13,
        [0x33] = 0x14,
        [0x34] = 0x15,
        [0x35] = 0x17,
        [0x36] = 0x16,
        [0x37] = 0x1A,
        [0x38] = 0x1C,
        [0x39] = 0x19,

        [0x41] = 0x00,
        [0x42] = 0x0B,
        [0x43] = 0x08,
        [0x44] = 0x02,
        [0x45] = 0x0E,
        [0x46] = 0x03,
        [0x47] = 0x05,
        [0x48] = 0x04,
        [0x49] = 0x22,
        [0x4A] = 0x26,
        [0x4B] = 0x28,
        [0x4C] = 0x25,
        [0x4D] = 0x2E,
        [0x4E] = 0x2D,
        [0x4F] = 0x1F,
        [0x50] = 0x23,
        [0x51] = 0x0C,
        [0x52] = 0x0F,
        [0x53] = 0x01,
        [0x54] = 0x11,
        [0x55] = 0x20,
        [0x56] = 0x09,
        [0x57] = 0x0D,
        [0x58] = 0x07,
        [0x59] = 0x10,
        [0x5A] = 0x06,

        [0x60] = 0x52,
        [0x61] = 0x53,
        [0x62] = 0x54,
        [0x63] = 0x55,
        [0x64] = 0x56,
        [0x65] = 0x57,
        [0x66] = 0x58,
        [0x67] = 0x59,
        [0x68] = 0x5B,
        [0x69] = 0x5C,
        [0x6A] = 0x43,
        [0x6B] = 0x45,
        [0x6D] = 0x4E,
        [0x6E] = 0x41,
        [0x6F] = 0x4B,

        [0x70] = 0x7A,
        [0x71] = 0x78,
        [0x72] = 0x63,
        [0x73] = 0x76,
        [0x74] = 0x60,
        [0x75] = 0x61,
        [0x76] = 0x62,
        [0x77] = 0x64,
        [0x78] = 0x65,
        [0x79] = 0x6D,
        [0x7A] = 0x67,
        [0x7B] = 0x6F,
        [0x7C] = 0x69,
        [0x7D] = 0x6B,
        [0x7E] = 0x71,
        [0x7F] = 0x6A,
        [0x80] = 0x40,
        [0x81] = 0x4F,
        [0x82] = 0x50,
        [0x83] = 0x5A,

        [0xBA] = 0x29,
        [0xBB] = 0x18,
        [0xBC] = 0x2B,
        [0xBD] = 0x1B,
        [0xBE] = 0x2F,
        [0xBF] = 0x2C,
        [0xC0] = 0x32,
        [0xDB] = 0x21,
        [0xDC] = 0x2A,
        [0xDD] = 0x1E,
        [0xDE] = 0x27,
        [0xE2] = 0x0A,
    };

    private static readonly Dictionary<int, int> ByCode =
        ByPlace.ToDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>
    /// The punctuation keys, by the character Windows names them after
    /// (VK_OEM_1...). Looked up by that character too, as the letters are.
    /// </summary>
    private static readonly Dictionary<int, char> Punctuation = new()
    {
        [0xBA] = ';',
        [0xBB] = '=',
        [0xBC] = ',',
        [0xBD] = '-',
        [0xBE] = '.',
        [0xBF] = '/',
        [0xC0] = '`',
        [0xDB] = '[',
        [0xDC] = '\\',
        [0xDD] = ']',
        [0xDE] = '\'',
    };

    private static readonly Dictionary<char, int> ByPunctuation =
        Punctuation.ToDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>
    /// The macOS key code of a Windows virtual key, or <c>null</c> when the
    /// Mac has no such key (Pause, F21 to F24...).
    /// </summary>
    /// <param name="virtualKey">The Windows virtual key.</param>
    /// <param name="keyTyping">
    /// The key code that types this character (a lowercase letter, a
    /// punctuation sign) with no modifier on the current layout, or
    /// <c>null</c> when none does.
    /// </param>
    public static int? ToKeyCode(int virtualKey, Func<char, int?> keyTyping)
    {
        ArgumentNullException.ThrowIfNull(keyTyping);

        char? typed = virtualKey is >= 0x41 and <= 0x5A
            ? char.ToLowerInvariant((char)virtualKey)
            : Punctuation.TryGetValue(virtualKey, out var sign) ? sign : null;

        if (typed is { } character && keyTyping(character) is { } code)
        {
            return code;
        }

        return ByPlace.TryGetValue(virtualKey, out var place) ? place : null;
    }

    /// <summary>
    /// The Windows virtual key of a macOS key code, or 0 when it has none.
    /// </summary>
    /// <param name="keyCode">The macOS key code.</param>
    /// <param name="characterOf">
    /// The character the key types with no modifier on the current layout,
    /// or <c>null</c>.
    /// </param>
    public static int ToVirtualKey(int keyCode, Func<int, char?> characterOf)
    {
        ArgumentNullException.ThrowIfNull(characterOf);

        // A key that types a letter or a named sign is that key, wherever it
        // stands; "é" or "&" on an AZERTY digit key leaves the place to
        // decide, which is the digit, as on Windows.
        if (characterOf(keyCode) is { } typed)
        {
            if (char.ToUpperInvariant(typed) is var letter and >= 'A' and <= 'Z')
            {
                return letter;
            }

            if (ByPunctuation.TryGetValue(typed, out var sign))
            {
                return sign;
            }
        }

        return ByCode.TryGetValue(keyCode, out var virtualKey) ? virtualKey : 0;
    }

    /// <summary>Carbon's flags for the PC's modifiers.</summary>
    public static uint ToCarbonModifiers(HotkeyModifiers modifiers)
    {
        uint native = 0;

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            native |= ControlKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            native |= OptionKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            native |= ShiftKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            native |= CmdKey;
        }

        return native;
    }
}
