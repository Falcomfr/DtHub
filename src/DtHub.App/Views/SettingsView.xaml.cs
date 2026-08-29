using System.Windows.Controls;
using System.Windows.Input;

using DtHub.App.ViewModels;

using DtHub.Core.Hotkeys;

namespace DtHub.App.Views;

/// <summary>
/// Page Paramètres. Le code-behind ne sert qu'à la capture des raccourcis :
/// c'est un événement clavier de bas niveau que XAML ne sait pas exprimer.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // La capture doit voir les touches avant que WPF ne les interprète,
        // sinon Tab changerait le focus au lieu d'être enregistrée.
        PreviewKeyDown += OnPreviewKeyDown;
        Focusable = true;
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel || viewModel.CapturingRow is null)
        {
            return;
        }

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            viewModel.CancelCaptureCommand.Execute(null);
            return;
        }

        // Tant que seule une touche de modification est enfoncée, on attend la
        // suite plutôt que de refuser la saisie.
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

        await viewModel.ApplyCapturedHotkeyAsync(KeyInterop.VirtualKeyFromKey(key), modifiers)
            .ConfigureAwait(true);
    }
}
