using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using DtHub.Core.Localization;

namespace DtHub.Mac.Platform;

/// <summary>
/// The menu bar's Edit and View menus, given to each of DT Hub's windows:
/// what Windows gives a WebView2 and a text field without a menu. On a Mac
/// Command+C, V, X, A and Z only reach a page through the Edit menu, sent
/// down the responder chain; an Avalonia field gets them here too, when
/// Avalonia did not handle the keys first. The View menu, on the windows
/// that show a page, carries the PC's Alt+Left, Alt+Right, F5 and
/// Ctrl+plus and minus, under the Mac's own keys.
/// </summary>
internal static class MacMenus
{
    public static void Install() =>
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Give(window));

    private static void Give(Window window)
    {
        if (NativeMenu.GetMenu(window) is not null)
        {
            return;
        }

        var menu = new NativeMenu();
        menu.Items.Add(Submenu("MacMenuEdit",
        [
            Item("MacMenuUndo", Key.Z, KeyModifiers.Meta, () => Edit(window, "undo:", box => box.Undo())),
            Item("MacMenuRedo", Key.Z, KeyModifiers.Meta | KeyModifiers.Shift, () => Edit(window, "redo:", box => box.Redo())),
            new NativeMenuItemSeparator(),
            Item("MacMenuCut", Key.X, KeyModifiers.Meta, () => Edit(window, "cut:", box => box.Cut())),
            Item("MacMenuCopy", Key.C, KeyModifiers.Meta, () => Edit(window, "copy:", box => box.Copy())),
            Item("MacMenuPaste", Key.V, KeyModifiers.Meta, () => Edit(window, "paste:", box => box.Paste())),
            Item("MacMenuSelectAll", Key.A, KeyModifiers.Meta, () => Edit(window, "selectAll:", box => box.SelectAll())),
        ]));

        // Only where a page shows: the Almanax reads its own out of sight.
        if (window.GetVisualDescendants().OfType<MacWebView>().Any(view => view.Height is not 0))
        {
            menu.Items.Add(Submenu("MacMenuView",
            [
                Item("MacMenuBack", Key.OemOpenBrackets, KeyModifiers.Meta, () => Page(window)?.GoBack()),
                Item("MacMenuForward", Key.OemCloseBrackets, KeyModifiers.Meta, () => Page(window)?.GoForward()),
                Item("MacMenuReload", Key.R, KeyModifiers.Meta, () => Page(window)?.Reload()),
                new NativeMenuItemSeparator(),
                Item("MacMenuActualSize", Key.D0, KeyModifiers.Meta, () => Page(window)?.SetZoom(1)),
                Item("MacMenuZoomIn", Key.OemPlus, KeyModifiers.Meta, () => Page(window)?.Zoom(1)),
                Item("MacMenuZoomOut", Key.OemMinus, KeyModifiers.Meta, () => Page(window)?.Zoom(-1)),
            ]));
        }

        NativeMenu.SetMenu(window, menu);
    }

    private static NativeMenuItem Submenu(string header, NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();

        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        return new NativeMenuItem(Strings.Get(header)) { Menu = menu };
    }

    private static NativeMenuItem Item(string header, Key key, KeyModifiers modifiers, Action action)
    {
        var item = new NativeMenuItem(Strings.Get(header)) { Gesture = new KeyGesture(key, modifiers) };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>To the page that has the keyboard, else to the window's focused field.</summary>
    private static void Edit(Window window, string selector, Action<TextBox> field)
    {
        if (MacWebView.TrySendEditAction(selector))
        {
            return;
        }

        if (window.FocusManager?.GetFocusedElement() is TextBox box)
        {
            field(box);
        }
    }

    /// <summary>The page the window shows, if it shows one now.</summary>
    private static MacWebView? Page(Window window) =>
        window.GetVisualDescendants().OfType<MacWebView>()
            .FirstOrDefault(view => view.IsReady && view.IsEffectivelyVisible && view.Bounds.Height > 0);
}
