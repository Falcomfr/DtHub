using DtHub.Core.Dofus;
using DtHub.Core.IPhone;
using DtHub.Core.Settings;

namespace DtHub.Tests.IPhone;

public class IosHdVisibilityTests
{
    [Fact]
    public void Un_joueur_sans_iPhone_ni_iPad_ne_voit_pas_l_image_HD()
    {
        var settings = new AppSettingsDocument { Instances = [new StoredInstance { Platform = DevicePlatform.Android }] };

        Assert.False(IosHdVisibility.Shows(settings));
    }

    [Fact]
    public void Un_compte_iPhone_ou_iPad_montre_l_image_HD()
    {
        var settings = new AppSettingsDocument
        {
            Instances = [new StoredInstance { Platform = DevicePlatform.Android }, new StoredInstance { Platform = DevicePlatform.Ios }],
        };

        Assert.True(IosHdVisibility.Shows(settings));
    }

    [Fact]
    public void Un_pilote_pose_reste_retirable_meme_sans_compte_iOS()
    {
        Assert.True(IosHdVisibility.Shows(new AppSettingsDocument { IosHdFilteredDevices = ["USB\\VID_05AC&PID_12A8"] }));
        Assert.True(IosHdVisibility.Shows(new AppSettingsDocument { IosHdPicture = true }));
    }
}
