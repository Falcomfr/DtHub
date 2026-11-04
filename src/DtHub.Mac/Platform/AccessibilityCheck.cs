using System.Diagnostics;

using DtHub.App.Services;
using DtHub.Core.Localization;

using Serilog;

namespace DtHub.Mac.Platform;

/// <summary>
/// Placing the game windows (grid, tabs, full screen, sizes) moves another
/// application's windows, which macOS allows only once the player has
/// ticked DT Hub in System Settings, Privacy &amp; Security, Accessibility.
/// Without it the games still open, each where scrcpy put it, and nothing
/// says why the layout does nothing: so it is asked at startup, with the
/// reason, and the setting is opened for the player.
/// </summary>
internal static class AccessibilityCheck
{
    private const string SettingsPane =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";

    public static void Run(IDialogService dialogs)
    {
        if (MacNative.dthub_ax_trusted(prompt: false))
        {
            return;
        }

        Log.Warning("Autorisation Accessibilité absente : les fenêtres de jeu ne seront pas rangées.");

        if (!dialogs.Confirm(Strings.Get("MacAccessibilityNeeded"), Strings.Get("MacAccessibilityTitle")))
        {
            return;
        }

        // Asking once with the prompt puts DT Hub in the list, unticked,
        // which is what the player then ticks: an app never asked is
        // missing from it.
        _ = MacNative.dthub_ax_trusted(prompt: true);
        Process.Start(new ProcessStartInfo("open") { ArgumentList = { SettingsPane }, UseShellExecute = false })?.Dispose();
    }
}
