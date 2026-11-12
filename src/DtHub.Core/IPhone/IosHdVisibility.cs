using DtHub.Core.Dofus;
using DtHub.Core.Settings;

namespace DtHub.Core.IPhone;

/// <summary>
/// Whether the settings show the Image HD box (D234): only to a player with an
/// iPhone or iPad account, so Android players never meet it, and to one who
/// still has it on or a driver to remove.
/// </summary>
public static class IosHdVisibility
{
    public static bool Shows(AppSettingsDocument settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.IosHdPicture
            || settings.IosHdFilteredDevices.Count > 0
            || settings.Instances.Any(i => i.Platform == DevicePlatform.Ios);
    }
}
