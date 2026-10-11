namespace DtHub.Core.Dofus;

/// <summary>
/// A DOFUS Touch companion app by MakeInTouch, shown with its maker's
/// agreement (2026-10-10).
/// </summary>
/// <param name="PackageName">Its Android package.</param>
/// <param name="Name">Its name, the same in every language.</param>
/// <param name="LineKey">The resource key of its one line presentation.</param>
/// <param name="IconAsset">The pack path of its bundled icon.</param>
public sealed record PartnerApp(string PackageName, string Name, string LineKey, string IconAsset);

/// <summary>Which connected devices have one partner app, and which lack it.</summary>
/// <param name="App">The app.</param>
/// <param name="WithApp">Device ids where it is installed.</param>
/// <param name="WithoutApp">Device ids where it is not.</param>
public sealed record PartnerAvailability(
    PartnerApp App,
    IReadOnlyList<string> WithApp,
    IReadOnlyList<string> WithoutApp);

/// <summary>
/// The two MakeInTouch apps DT Hub presents. Constants, not a setting: the
/// spec names these two and no others.
/// </summary>
public static class PartnerApps
{
    public static IReadOnlyList<PartnerApp> All { get; } =
    [
        new("com.makeintouch.dtstuff", "DTStuff", "PartnerDtStuffLine", "/assets/dtstuff.png"),
        new("com.makeintouch.ocrapp", "OcrApp", "PartnerOcrAppLine", "/assets/ocrapp.png"),
    ];

    public static bool IsPartner(string? packageName) =>
        All.Any(a => string.Equals(a.PackageName, packageName, StringComparison.Ordinal));

    /// <summary>The app's page in the Play Store, as Android opens it.</summary>
    public static string StoreUri(string packageName) => $"market://details?id={packageName}";

    /// <summary>
    /// The same availability, with one app now installed on one device:
    /// what a store window learns before the next sweep does.
    /// </summary>
    public static IReadOnlyList<PartnerAvailability> WithInstalled(
        IReadOnlyList<PartnerAvailability> availability,
        string packageName,
        string deviceId) =>
        [.. availability.Select(a =>
            !string.Equals(a.App.PackageName, packageName, StringComparison.Ordinal)
            || a.WithApp.Contains(deviceId, StringComparer.Ordinal)
                ? a
                : a with
                {
                    WithApp = [.. a.WithApp, deviceId],
                    WithoutApp = [.. a.WithoutApp.Where(d => !string.Equals(d, deviceId, StringComparison.Ordinal))],
                })];

    /// <summary>
    /// For each app, in <see cref="All" /> order, the devices that have it
    /// and those that do not.
    /// </summary>
    /// <param name="devices">
    /// Each connected device with the packages of its main profile, user 0.
    /// A clone profile is left out on purpose: the apps are opened from the
    /// main one.
    /// </param>
    public static IReadOnlyList<PartnerAvailability> Availability(
        IReadOnlyList<(string DeviceId, IReadOnlyList<string> PackagesUser0)> devices) =>
        [.. All.Select(app => new PartnerAvailability(
            app,
            [.. devices.Where(d => d.PackagesUser0.Contains(app.PackageName, StringComparer.Ordinal)).Select(d => d.DeviceId)],
            [.. devices.Where(d => !d.PackagesUser0.Contains(app.PackageName, StringComparer.Ordinal)).Select(d => d.DeviceId)]))];
}

/// <summary>
/// The DOFUS Touch map, first of the tools: a web site shown in a DT Hub
/// window, which needs no phone. Its logo is drawn for DT Hub, not the
/// site's own.
/// </summary>
public static class DofusTouchMap
{
    /// <summary>The site, as given by the project owner on 2026-10-11.</summary>
    public const string Address = "https://www.dofus-touch-map.com/";

    /// <summary>The pack path of its bundled logo.</summary>
    public const string IconAsset = "/assets/dtmap.png";
}
