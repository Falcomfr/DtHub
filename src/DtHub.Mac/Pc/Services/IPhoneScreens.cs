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

    public IReadOnlyList<IGameWindow> Windows => [];

    public event EventHandler? Changed
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

    public bool CountsPlaytime(string key) => false;

    public IosWindowState? StateOf(string key) => null;

    public bool IsGameMissing(string key) => false;

    public string? ProblemOf(string key) => null;

    public void Unmaximize()
    {
    }

    public Task RefreshAsync() => Task.CompletedTask;

    public void Show(IReadOnlyList<IPhoneDevice> devices)
    {
    }

    public void SetDark(bool dark)
    {
    }

    public bool ToggleDark(nint foreground) => false;

    public bool ToggleDarkOf(string key) => false;
}
