using DtHub.Core.Adb;

namespace DtHub.Tests.Adb;

/// <summary>
/// Les sorties employées ici sont relevées sur le téléphone de référence, un
/// Xiaomi 23078PND5G sous Android 15, ou reprises telles qu'Android les écrit
/// dans ses sources.
/// </summary>
public class AdbErrorInterpreterTests
{
    [Fact]
    public void Un_refus_de_permission_ne_passe_plus_pour_une_application_absente()
    {
        // Le message que rend le Dossier sécurisé de Samsung, et tout profil
        // tenu par une politique d'entreprise. Le jeu y est bien installé :
        // dire « l'application n'est plus installée » envoyait le joueur
        // réinstaller ce qui est déjà là.
        const string output =
            "java.lang.SecurityException: Permission Denial: startActivity asks to run as "
            + "user 15 but is calling from user 0";

        Assert.Equal(AdbErrorKind.PermissionDenied, AdbErrorInterpreter.Classify(output));
    }

    [Fact]
    public void Un_profil_en_pause_est_nomme_pour_ce_qu_il_est()
    {
        // L'état ordinaire de Shelter et d'Island, et celui d'un profil
        // professionnel dont l'interrupteur est éteint.
        const string output = "Error: Activity not started, user 10 is in quiet mode";

        Assert.Equal(AdbErrorKind.ProfilePaused, AdbErrorInterpreter.Classify(output));
    }

    [Fact]
    public void La_pause_passe_avant_le_reste()
    {
        // Un profil en pause rend parfois un message qui parle aussi du
        // paquet. C'est la pause qui explique la panne, et c'est elle qui
        // doit être dite.
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
        // Relevé réel : la réponse du téléphone quand on demande un second
        // profil géré. Rien là-dedans ne dit que le jeu est absent, et le
        // supposer était le défaut corrigé.
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
}
