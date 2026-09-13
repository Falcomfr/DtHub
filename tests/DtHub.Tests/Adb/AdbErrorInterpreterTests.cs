using DtHub.Core.Adb;

namespace DtHub.Tests.Adb;

/// <summary>
/// The outputs used here were captured on the reference phone, a Xiaomi
/// 23078PND5G running Android 15, or copied exactly as Android writes
/// them in its own sources.
/// </summary>
public class AdbErrorInterpreterTests
{
    [Fact]
    public void Un_refus_de_permission_ne_passe_plus_pour_une_application_absente()
    {
        // The message returned by Samsung's Secure Folder, and by any
        // profile managed under a corporate policy. The game is indeed
        // installed there: saying "the app is no longer installed" used
        // to send the player off to reinstall something already there.
        const string output =
            "java.lang.SecurityException: Permission Denial: startActivity asks to run as "
            + "user 15 but is calling from user 0";

        Assert.Equal(AdbErrorKind.PermissionDenied, AdbErrorInterpreter.Classify(output));
    }

    [Fact]
    public void Un_profil_en_pause_est_nomme_pour_ce_qu_il_est()
    {
        // The ordinary state of Shelter and Island, and that of a work
        // profile whose switch is turned off.
        const string output = "Error: Activity not started, user 10 is in quiet mode";

        Assert.Equal(AdbErrorKind.ProfilePaused, AdbErrorInterpreter.Classify(output));
    }

    [Fact]
    public void La_pause_passe_avant_le_reste()
    {
        // A paused profile sometimes returns a message that also
        // mentions the package. It is the pause that explains the
        // failure, and it is the pause that must be reported.
        const string output =
            "Error: Package com.exemple does not exist for user 10, user is in quiet mode";

        Assert.Equal(AdbErrorKind.ProfilePaused, AdbErrorInterpreter.Classify(output));
    }

    [Fact]
    public void Une_application_reellement_absente_reste_reconnue()
    {
        const string output = "Error: Unknown package: com.exemple";

        Assert.Equal(AdbErrorKind.PackageNotFound, AdbErrorInterpreter.Classify(output));
    }

    [Fact]
    public void Une_erreur_inconnue_reste_inconnue()
    {
        // Real capture: the phone's response when a second managed
        // profile is requested. Nothing in it says the game is absent,
        // and assuming so was the bug that got fixed.
        const string output =
            "Error: android.os.ServiceSpecificException: Cannot add more profiles of type "
            + "android.os.usertype.profile.MANAGED for user 0 (code 6)";

        Assert.Null(AdbErrorInterpreter.Classify(output));
    }

    [Fact]
    public void Chaque_famille_a_une_phrase_montrable()
    {
        foreach (var kind in Enum.GetValues<AdbErrorKind>())
        {
            Assert.NotEmpty(AdbErrorInterpreter.Describe(kind));
        }
    }
    [Fact]
    public void Un_shell_qui_n_atteint_pas_un_profil_a_sa_propre_famille()
    {
        // String captured character for character on a competing
        // product's support forum, which follows the same path as we
        // do: pm create-user then pm install-existing. User 150 is
        // Samsung's Secure Folder, which must be unlocked first.
        const string sortie =
            "Exception occurred while executing 'install-existing': "
            + "java.lang.SecurityException: Shell does not have permission to access user 150";

        Assert.Equal(AdbErrorKind.ShellUserAccessDenied, AdbErrorInterpreter.Classify(sortie));
    }

    [Fact]
    public void Le_refus_de_permission_ordinaire_reste_range_comme_avant()
    {
        // The order of the patterns is everything: the new family is
        // recognized before the generic denial, and must not swallow
        // it in the process.
        Assert.Equal(
            AdbErrorKind.PermissionDenied,
            AdbErrorInterpreter.Classify("java.lang.SecurityException: Permission Denial: broadcast"));
    }

    [Fact]
    public void Les_deux_familles_ne_disent_pas_la_meme_chose()
    {
        var profil = AdbErrorInterpreter.Describe(AdbErrorKind.ShellUserAccessDenied);
        var permission = AdbErrorInterpreter.Describe(AdbErrorKind.PermissionDenied);

        Assert.NotEqual(permission, profil);

        // The message names the two remedies, which have nothing to do
        // with one another: unlocking the secure folder, or the
        // debugging security setting.
        Assert.Contains("sécurité", profil, StringComparison.OrdinalIgnoreCase);
    }

}
