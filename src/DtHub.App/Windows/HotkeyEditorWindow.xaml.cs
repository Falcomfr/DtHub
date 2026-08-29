using System.Windows;
using System.Windows.Input;

using DtHub.App.ViewModels;
using DtHub.Core.Hotkeys;

namespace DtHub.App.Windows;

/// <summary>
/// Éditeur de raccourcis. Le code-behind ne sert qu'à la capture des touches,
/// que XAML ne sait pas exprimer.
/// </summary>
public partial class HotkeyEditorWindow : Window
{
    private readonly HotkeyEditorViewModel _viewModel;

    public HotkeyEditorWindow(HotkeyEditorViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;

        // La capture doit voir les touches avant que WPF ne les interprète,
        // sinon Tab changerait le focus au lieu d'être enregistrée.
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

        await _viewModel.ApplyCapturedAsync(KeyInterop.VirtualKeyFromKey(key), modifiers)
            .ConfigureAwait(true);
    }
}
