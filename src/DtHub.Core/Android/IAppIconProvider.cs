namespace DtHub.Core.Android;

/// <summary>What is needed to fetch an icon from a specific phone.</summary>
/// <param name="DeviceId">
/// Stable identity of the device, used as the key.
/// </param>
/// <param name="Serial">
/// ADB serial number, which is a wireless address.
/// </param>
/// <param name="UserId">
/// Profile in which to look for the package. An application
/// installed only on the cloned profile is invisible to
/// <c>pm path</c> without it. The result itself does not depend on
/// the profile: it is the same archive for everyone.
/// </param>
/// <param name="PackageName">Package whose icon is wanted.</param>
public readonly record struct AppIconRequest(
    string DeviceId,
    string Serial,
    int UserId,
    string PackageName);

/// <summary>
/// Returns the icon of an application as it is on the phone,
/// extracted from a single entry of its archive and placed into the
/// cache.
///
/// Never throws. An icon is decoration: neither a phone without
/// <c>unzip</c>, nor an archive without a raster image, nor an ADB
/// refusal justify failing at anything. In all these cases the
/// method returns <c>null</c>, and the list displays exactly as it
/// would without it.
/// </summary>
public interface IAppIconProvider
{
    /// <summary>
    /// What is already known, without asking the phone anything. This
    /// is what the scan calls: it runs every three seconds and must not
    /// grow by a single extra command.
    /// </summary>
    string? Find(string deviceId, string packageName);

    /// <summary>
    /// The icon's path, extracted if needed, or <c>null</c>. Never
    /// throws, which is what allows the caller not to await it.
    /// </summary>
    Task<string?> GetAsync(AppIconRequest request, CancellationToken cancellationToken = default);
}
