using DtHub.Core.Hotkeys;

using Microsoft.Extensions.Logging;

namespace DtHub.Mac.Platform;

/// <summary>
/// The global shortcuts. shortcut: none is registered with macOS yet
/// (Carbon's RegisterEventHotKey is the way, planned); the set is kept and
/// reported as accepted, so the editor and the reminders behave as on the
/// PC, and the log says they do not fire.
/// </summary>
internal sealed partial class MacHotkeyRegistrar(ILogger<MacHotkeyRegistrar> logger) : IHotkeyRegistrar
{
#pragma warning disable CS0067 // Raised once the shortcuts reach macOS.
    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public event EventHandler<nint>? ForegroundWindowChanged;

    public event EventHandler<nint>? MouseCaptured;
#pragma warning restore CS0067

    public bool IsEnabled { get; private set; }

    public Task<IReadOnlyList<HotkeyAction>> ApplyAsync(HotkeySet hotkeys)
    {
        LogNotYet();
        return Task.FromResult<IReadOnlyList<HotkeyAction>>([]);
    }

    public Task SetEnabledAsync(bool enabled)
    {
        IsEnabled = enabled;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Raccourcis globaux pas encore actifs sur Mac.")]
    private partial void LogNotYet();
}
