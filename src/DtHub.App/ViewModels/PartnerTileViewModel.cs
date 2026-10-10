using System.Windows.Input;

using DtHub.Core.Dofus;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>A phone the tile's button can act on, for its menu.</summary>
/// <param name="DeviceName">The phone's name, as the list shows it.</param>
/// <param name="Act">Installs or opens the app on that phone.</param>
public sealed record PartnerTarget(string DeviceName, ICommand Act);

/// <summary>
/// One MakeInTouch app in the block at the bottom of the Devices tab: a
/// compact line with Open once it is on a phone, a presentation with
/// Install otherwise.
/// </summary>
public sealed class PartnerTileViewModel(PartnerApp app, bool isInstalled, IReadOnlyList<PartnerTarget> targets)
{
    public string Name => app.Name;

    public string Line => Strings.Get(app.LineKey);

    public string Icon => app.IconAsset;

    public bool IsInstalled => isInstalled;

    public string ActionText => Strings.Get(isInstalled ? "PartnerOpen" : "PartnerInstall");

    public IReadOnlyList<PartnerTarget> Targets => targets;

    /// <summary>False with no phone to act on: the button greys out.</summary>
    public bool CanAct => targets.Count > 0;
}
