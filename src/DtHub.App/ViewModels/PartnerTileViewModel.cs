using System.Windows.Input;

using DtHub.Core.Dofus;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>A phone the tile can act on, one button each.</summary>
/// <param name="DeviceName">The phone's name, as the list shows it.</param>
/// <param name="Act">Installs or opens the app on that phone.</param>
public sealed record PartnerTarget(string DeviceName, ICommand Act)
{
    /// <summary>The button's text, set by the tile: it knows whether there is a choice.</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// One MakeInTouch app in the block at the bottom of the Devices tab: a
/// compact line with Open once it is on a phone, a presentation with
/// Install otherwise.
/// </summary>
public sealed class PartnerTileViewModel(
    string name,
    string line,
    string icon,
    bool isInstalled,
    IReadOnlyList<PartnerTarget> targets)
{
    /// <summary>A MakeInTouch app, from the catalogue.</summary>
    public PartnerTileViewModel(PartnerApp app, bool isInstalled, IReadOnlyList<PartnerTarget> targets)
        : this(app.Name, Strings.Get(app.LineKey), app.IconAsset, isInstalled, targets)
    {
    }

    public string Name => name;

    public string Line => line;

    public string Icon => icon;

    /// <summary>
    /// Under the name: a reason when no phone can act, the one line
    /// presentation otherwise.
    /// </summary>
    public string Detail => targets.Count == 0 ? Strings.Get("PartnerNoDevice") : line;

    public bool IsInstalled => isInstalled;

    public string ActionText => Strings.Get(isInstalled ? "PartnerOpen" : "PartnerInstall");

    /// <summary>
    /// The buttons: "Install" alone for one phone, "Install · phone" for
    /// each when there is a choice.
    /// </summary>
    public IReadOnlyList<PartnerTarget> Targets { get; } =
        [.. targets.Select(t => t with
        {
            Label = targets.Count == 1
                ? Strings.Get(isInstalled ? "PartnerOpen" : "PartnerInstall")
                : Strings.Format("PartnerTargetLabel", Strings.Get(isInstalled ? "PartnerOpen" : "PartnerInstall"), t.DeviceName),
        })];

    /// <summary>False with no phone to act on: the button greys out.</summary>
    public bool CanAct => targets.Count > 0;
}
