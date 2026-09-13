namespace DtHub.Core.Hotkeys;

/// <summary>
/// Registers hotkeys with the system. Registration is deliberately
/// conditional: outside DT Hub's windows, the combinations must
/// revert to other software, otherwise Ctrl+Tab would stop working
/// in a browser.
/// </summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>Fired when a registered combination is pressed.</summary>
    event EventHandler<HotkeyAction>? HotkeyPressed;

    /// <summary>
    /// Fired when the desktop's active window changes. The caller
    /// then decides whether to enable or disable the hotkeys.
    /// </summary>
    event EventHandler<nint>? ForegroundWindowChanged;

    /// <summary>True if the hotkeys are currently registered.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Sets the hotkeys to register. Takes effect immediately if
    /// the hotkeys are active.
    /// </summary>
    /// <returns>
    /// Actions whose hotkey was refused by the system, usually
    /// because another program already holds it.
    /// </returns>
    Task<IReadOnlyList<HotkeyAction>> ApplyAsync(HotkeySet hotkeys);

    /// <summary>Enables or disables interception.</summary>
    Task SetEnabledAsync(bool enabled);
}
