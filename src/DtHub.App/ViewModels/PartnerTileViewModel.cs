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
/// One tool in the tools menu, a tile of its icon and name: a click opens
/// it, or installs it where it is missing. Its presentation is the tile's
/// tooltip, so the menu stays one row.
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
    /// The phones it can act on. With one, the tile's click acts; with
    /// several, each is offered by name under the grid.
    /// </summary>
    public IReadOnlyList<PartnerTarget> Targets { get; } =
        [.. targets.Select(t => t with { Label = t.DeviceName })];

    /// <summary>
    /// Under the name, only when the click will not simply open: the app
    /// is to install, or no phone can take it.
    /// </summary>
    public string? Badge =>
        targets.Count == 0 ? Strings.Get("PartnerNoDeviceShort")
        : isInstalled ? null
        : Strings.Get("PartnerInstall");

    /// <summary>The question above the phones, when there is a choice.</summary>
    public string PickPrompt => Strings.Format("PartnerPickDevice", ActionText);

    /// <summary>Turns the app's windows, for an app that lays itself out in landscape too.</summary>
    public ICommand? Turn { get; init; }

    public bool CanTurn => Turn is not null;

    /// <summary>The turn button's tooltip: the orientation it gives.</summary>
    public string? TurnTip { get; init; }

        /// <summary>False with no phone to act on: the button greys out.</summary>
    public bool CanAct => targets.Count > 0;
}
