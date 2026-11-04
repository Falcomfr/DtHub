using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using DtHub.App.ViewModels;
using DtHub.Core.Hotkeys;
using DtHub.Mac.Platform;

namespace DtHub.Mac.Views;

/// <summary>
/// The hotkey editor, the PC's HotkeyEditorWindow
/// (src/DtHub.App/Windows/HotkeyEditorWindow.xaml.cs). The code-behind is
/// only the key capture, which AXAML cannot express.
///
/// A key is read by its place on the keyboard (Avalonia's PhysicalKey),
/// turned into the Mac's key code, then into the PC's model by
/// <see cref="MacKeyMap"/>, which asks the current layout for letters:
/// the key printed A on an AZERTY keyboard is saved as A, and pressed
/// there again when the shortcut is registered. While this window holds
/// the keyboard the shortcuts are off, as on the PC (it is not one of the
/// windows the launcher counts as ours), so the current ones can be typed.
/// </summary>
public partial class HotkeyEditorWindow : Window
{
    private readonly HotkeyEditorViewModel _viewModel = null!;

    /// <summary>For the XAML previewer only.</summary>
    public HotkeyEditorWindow() => AvaloniaXamlLoader.Load(this);

    public HotkeyEditorWindow(HotkeyEditorViewModel viewModel)
    {
        _viewModel = viewModel;

        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;

        // Capture must see the keys before Avalonia interprets them,
        // otherwise Tab would change focus instead of being recorded.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        Opened += async (_, _) => await _viewModel.LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel.CapturingRow is null)
        {
            return;
        }

        e.Handled = true;

        if (e.PhysicalKey == PhysicalKey.Escape || e.Key == Key.Escape)
        {
            _viewModel.CancelCaptureCommand.Execute(null);
            return;
        }

        // As long as only a modifier key is pressed, we wait for what comes
        // next rather than rejecting the input.
        if (e.PhysicalKey is PhysicalKey.ControlLeft or PhysicalKey.ControlRight
            or PhysicalKey.ShiftLeft or PhysicalKey.ShiftRight
            or PhysicalKey.AltLeft or PhysicalKey.AltRight
            or PhysicalKey.MetaLeft or PhysicalKey.MetaRight
            || e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        // A key with no place here (a media key...) gives 0, which the view
        // model refuses as "no key", where the player just typed.
        var virtualKey = MacKeyCodes.TryGetValue(e.PhysicalKey, out var code)
            ? MacKeyMap.ToVirtualKey(code, MacKeyboard.CharacterOf)
            : 0;

        await _viewModel.ApplyCapturedAsync(virtualKey, modifiers).ConfigureAwait(true);
    }

    /// <summary>Avalonia's key places to the Mac's key codes (Events.h).</summary>
    private static readonly Dictionary<PhysicalKey, int> MacKeyCodes = new()
    {
        [PhysicalKey.A] = 0x00,
        [PhysicalKey.S] = 0x01,
        [PhysicalKey.D] = 0x02,
        [PhysicalKey.F] = 0x03,
        [PhysicalKey.H] = 0x04,
        [PhysicalKey.G] = 0x05,
        [PhysicalKey.Z] = 0x06,
        [PhysicalKey.X] = 0x07,
        [PhysicalKey.C] = 0x08,
        [PhysicalKey.V] = 0x09,
        [PhysicalKey.IntlBackslash] = 0x0A,
        [PhysicalKey.B] = 0x0B,
        [PhysicalKey.Q] = 0x0C,
        [PhysicalKey.W] = 0x0D,
        [PhysicalKey.E] = 0x0E,
        [PhysicalKey.R] = 0x0F,
        [PhysicalKey.Y] = 0x10,
        [PhysicalKey.T] = 0x11,
        [PhysicalKey.Digit1] = 0x12,
        [PhysicalKey.Digit2] = 0x13,
        [PhysicalKey.Digit3] = 0x14,
        [PhysicalKey.Digit4] = 0x15,
        [PhysicalKey.Digit6] = 0x16,
        [PhysicalKey.Digit5] = 0x17,
        [PhysicalKey.Equal] = 0x18,
        [PhysicalKey.Digit9] = 0x19,
        [PhysicalKey.Digit7] = 0x1A,
        [PhysicalKey.Minus] = 0x1B,
        [PhysicalKey.Digit8] = 0x1C,
        [PhysicalKey.Digit0] = 0x1D,
        [PhysicalKey.BracketRight] = 0x1E,
        [PhysicalKey.O] = 0x1F,
        [PhysicalKey.U] = 0x20,
        [PhysicalKey.BracketLeft] = 0x21,
        [PhysicalKey.I] = 0x22,
        [PhysicalKey.P] = 0x23,
        [PhysicalKey.Enter] = 0x24,
        [PhysicalKey.L] = 0x25,
        [PhysicalKey.J] = 0x26,
        [PhysicalKey.Quote] = 0x27,
        [PhysicalKey.K] = 0x28,
        [PhysicalKey.Semicolon] = 0x29,
        [PhysicalKey.Backslash] = 0x2A,
        [PhysicalKey.Comma] = 0x2B,
        [PhysicalKey.Slash] = 0x2C,
        [PhysicalKey.N] = 0x2D,
        [PhysicalKey.M] = 0x2E,
        [PhysicalKey.Period] = 0x2F,
        [PhysicalKey.Tab] = 0x30,
        [PhysicalKey.Space] = 0x31,
        [PhysicalKey.Backquote] = 0x32,
        [PhysicalKey.Backspace] = 0x33,
        [PhysicalKey.CapsLock] = 0x39,

        [PhysicalKey.NumPadDecimal] = 0x41,
        [PhysicalKey.NumPadMultiply] = 0x43,
        [PhysicalKey.NumPadAdd] = 0x45,
        [PhysicalKey.NumPadDivide] = 0x4B,
        [PhysicalKey.NumPadEnter] = 0x4C,
        [PhysicalKey.NumPadSubtract] = 0x4E,
        [PhysicalKey.NumPad0] = 0x52,
        [PhysicalKey.NumPad1] = 0x53,
        [PhysicalKey.NumPad2] = 0x54,
        [PhysicalKey.NumPad3] = 0x55,
        [PhysicalKey.NumPad4] = 0x56,
        [PhysicalKey.NumPad5] = 0x57,
        [PhysicalKey.NumPad6] = 0x58,
        [PhysicalKey.NumPad7] = 0x59,
        [PhysicalKey.NumPad8] = 0x5B,
        [PhysicalKey.NumPad9] = 0x5C,

        [PhysicalKey.F1] = 0x7A,
        [PhysicalKey.F2] = 0x78,
        [PhysicalKey.F3] = 0x63,
        [PhysicalKey.F4] = 0x76,
        [PhysicalKey.F5] = 0x60,
        [PhysicalKey.F6] = 0x61,
        [PhysicalKey.F7] = 0x62,
        [PhysicalKey.F8] = 0x64,
        [PhysicalKey.F9] = 0x65,
        [PhysicalKey.F10] = 0x6D,
        [PhysicalKey.F11] = 0x67,
        [PhysicalKey.F12] = 0x6F,
        [PhysicalKey.F13] = 0x69,
        [PhysicalKey.F14] = 0x6B,
        [PhysicalKey.F15] = 0x71,
        [PhysicalKey.F16] = 0x6A,
        [PhysicalKey.F17] = 0x40,
        [PhysicalKey.F18] = 0x4F,
        [PhysicalKey.F19] = 0x50,
        [PhysicalKey.F20] = 0x5A,

        [PhysicalKey.Insert] = 0x72,
        [PhysicalKey.Home] = 0x73,
        [PhysicalKey.PageUp] = 0x74,
        [PhysicalKey.Delete] = 0x75,
        [PhysicalKey.End] = 0x77,
        [PhysicalKey.PageDown] = 0x79,
        [PhysicalKey.ArrowLeft] = 0x7B,
        [PhysicalKey.ArrowRight] = 0x7C,
        [PhysicalKey.ArrowDown] = 0x7D,
        [PhysicalKey.ArrowUp] = 0x7E,
    };
}
