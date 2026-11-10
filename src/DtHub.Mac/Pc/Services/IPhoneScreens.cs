using System.Collections.ObjectModel;

using DtHub.App.ViewModels;
using DtHub.Core.IPhone;

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
    public ObservableCollection<IPhoneRowViewModel> Devices { get; } = [];

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public int OpenCount => 0;

    public IReadOnlyList<(nint Handle, double Aspect)> TakeScreensForArranging() => [];

    public Task RefreshAsync() => Task.CompletedTask;

    public void Show(IReadOnlyList<IPhoneDevice> devices)
    {
    }
}
