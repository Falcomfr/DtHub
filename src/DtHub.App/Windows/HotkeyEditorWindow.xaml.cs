using System.Windows;
using System.Windows.Input;

using DtHub.App.ViewModels;
using DtHub.Core.Hotkeys;

namespace DtHub.App.Windows;

/// <summary>
/// Hotkey editor. The code-behind is only used for key capture,
/// which XAML cannot express.
/// </summary>
public partial class HotkeyEditorWindow : Window
{
    private readonly HotkeyEditorViewModel _viewModel;

    public HotkeyEditorWindow(HotkeyEditorViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;

        // Capture must see the keys before WPF interprets them,
        // otherwise Tab would change focus instead of being recorded.
        PreviewKeyDown += OnPreviewKeyDown;

        Loaded += async (_, _) => await _viewModel.LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.CapturingRow is null)
        {
            return;
        }

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _viewModel.CancelCaptureCommand.Execute(null);
            return;
        }

        // As long as only a modifier key is pressed, we wait for
        // what comes next rather than rejecting the input.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        await _viewModel.ApplyCapturedAsync(KeyInterop.VirtualKeyFromKey(key), modifiers)
            .ConfigureAwait(true);
    }
}
