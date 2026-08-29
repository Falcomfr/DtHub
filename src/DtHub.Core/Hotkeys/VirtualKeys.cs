using System.Globalization;

namespace DtHub.Core.Hotkeys;

/// <summary>
/// Codes de touches virtuelles Windows et leur libellé. Les raccourcis sont
/// stockés sous forme de code plutôt que d'énumération WPF, pour que le noyau
/// reste indépendant de l'interface.
/// </summary>
public static class VirtualKeys
{
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Alt = 0x12;
    public const int Pause = 0x13;
    public const int CapsLock = 0x14;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int PageUp = 0x21;
    public const int PageDown = 0x22;
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Insert = 0x2D;
    public const int Delete = 0x2E;
    public const int D0 = 0x30;
    public const int D1 = 0x31;
    public const int D2 = 0x32;
    public const int D3 = 0x33;
    public const int D4 = 0x34;
    public const int D5 = 0x35;
    public const int A = 0x41;
    public const int R = 0x52;
    public const int LeftWindows = 0x5B;
    public const int RightWindows = 0x5C;
    public const int NumPad0 = 0x60;
    public const int F1 = 0x70;
    public const int F5 = 0x74;
    public const int F9 = 0x78;
    public const int F12 = 0x7B;
    public const int F24 = 0x87;

    /// <summary>
    /// Vrai si le code désigne une touche de modification. Une telle touche ne
    /// peut pas servir de touche principale à un raccourci.
    /// </summary>
    public static bool IsModifierKey(int virtualKey) => virtualKey switch
    {
        Shift or Control or Alt or LeftWindows or RightWindows => true,
        >= 0xA0 and <= 0xA5 => true,
        _ => false,
    };

    /// <summary>Vrai si le code désigne une touche de fonction.</summary>
    public static bool IsFunctionKey(int virtualKey) => virtualKey is >= F1 and <= F24;

    /// <summary>Libellé affiché pour une touche.</summary>
    public static string Describe(int virtualKey) => virtualKey switch
    {
        0 => "(aucune)",
        Back => "Retour",
        Tab => "Tab",
        Enter => "Entrée",
        Pause => "Pause",
        CapsLock => "Verr. Maj",
        Escape => "Échap",
        Space => "Espace",
        PageUp => "Page préc.",
        PageDown => "Page suiv.",
        End => "Fin",
        Home => "Origine",
        Left => "Gauche",
        Up => "Haut",
        Right => "Droite",
        Down => "Bas",
        Insert => "Inser",
        Delete => "Suppr",
        >= D0 and <= 0x39 => ((char)virtualKey).ToString(),
        >= A and <= 0x5A => ((char)virtualKey).ToString(),
        >= NumPad0 and <= 0x69 => "Pavé " + (virtualKey - NumPad0).ToString(CultureInfo.InvariantCulture),
        0x6A => "Pavé *",
        0x6B => "Pavé +",
        0x6D => "Pavé -",
        0x6E => "Pavé .",
        0x6F => "Pavé /",
        >= F1 and <= F24 => "F" + (virtualKey - F1 + 1).ToString(CultureInfo.InvariantCulture),
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "²",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => "Touche " + virtualKey.ToString(CultureInfo.InvariantCulture),
    };
}
