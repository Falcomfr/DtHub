using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// What DT Hub requires from the device, and how to say so before
/// trying.
///
/// The requirement is not ours: it is the virtual display's, which
/// Android only exposes starting with version 11. Without this
/// check, an older device goes all the way through the launch,
/// waits out the full timeout, and receives a message guessed from
/// scrcpy's English output, sometimes the wrong one. The API level
/// is already read at discovery: it may as well be put to use.
/// </summary>
public static class AndroidRequirements
{
    /// <summary>
    /// API level of Android 11, where the virtual display appears.
    /// </summary>
    public const int VirtualDisplaySdk = 30;

    /// <summary>Name of this version, as it is said to the user.</summary>
    public const string VirtualDisplayVersion = "Android 11";

    /// <summary>
    /// Reason why this device cannot open a game window, or
    /// <c>null</c> if it can.
    ///
    /// Also returns <c>null</c> when the API level could not be
    /// read: a device is not refused out of ignorance, it is
    /// allowed to try.
    /// </summary>
    public static string? DescribeVirtualDisplayShortfall(int? sdkVersion, string? androidVersion)
    {
        if (sdkVersion is not { } sdk || sdk >= VirtualDisplaySdk)
        {
            return null;
        }

        return Strings.Format(
            "AndroidTooOld", Describe(sdk, androidVersion), VirtualDisplayVersion);
    }

    private static string Describe(int sdk, string? androidVersion) =>
        string.IsNullOrWhiteSpace(androidVersion)
            ? Strings.Get("ApiLevelPrefix") + sdk.ToString(CultureInfo.InvariantCulture)
            : "Android " + androidVersion.Trim();
}
