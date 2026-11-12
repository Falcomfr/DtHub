using DtHub.Core.Dofus;
using DtHub.Core.IPhone;
using DtHub.Core.Windows;

namespace DtHub.App.Services;

// Instance members, as on the PC: the shared code calls them on the injected service.
#pragma warning disable CA1822

/// <summary>
/// The PC's IPhoneScreens (src/DtHub.App/Services/IPhoneScreens.cs) opens
/// WPF windows: the iPhone is not wired on the Mac (D227), so no iPhone is
/// ever listed here. Same contract, for the shared list and launcher.
/// </summary>
public sealed class IPhoneScreens
{
    public IReadOnlyList<IPhoneDevice> Listed => [];

    public bool ListFailed => false;

    public IReadOnlyList<IGameWindow> Windows => [];

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public event EventHandler? ListChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? Opened
    {
        add { }
        remove { }
    }

    public event EventHandler<(string Key, IosEnd End)>? Ended
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? AspectChanged
    {
        add { }
        remove { }
    }

    public void SetDocked(string key, bool docked)
    {
    }

    public void Blur(string key)
    {
    }

    public bool CountsPlaytime(string key) => false;

    public bool AnyKeepsAppAlive => false;

    public IosWindowState? StateOf(string key) => null;

    public bool IsGameMissing(string key) => false;

    public string? ProblemOf(string key) => null;

    public void Unmaximize()
    {
    }

    public Task RefreshAsync() => Task.CompletedTask;

    public Task ListNowAsync() => Task.CompletedTask;

    public Task OpenAsync(DofusInstance account, IPhoneDevice device, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CloseAsync(string key, bool byHand, bool stopCapture) => Task.CompletedTask;

    public Task StopGameAsync(string key) => Task.CompletedTask;

    public Task RestartGameAsync(string key) => Task.CompletedTask;

    public void Show(IReadOnlyList<IPhoneDevice> devices)
    {
    }

    public void ForgetHdFailures()
    {
    }

    public void SetDark(bool dark)
    {
    }

    public bool ToggleDark(nint foreground) => false;

    public bool ToggleDarkOf(string key) => false;
}
