using DtHub.Core.Adb;

namespace DtHub.Tests.Adb;

public class AdbConnectFailureTests
{
    /// <summary>
    /// Captured character-for-character on a French machine,
    /// platform-tools 37.0.1, against a real phone. The system
    /// messages are Windows's own, in French, accents and
    /// typographic apostrophes included: this is precisely what an
    /// overly hasty parser would break on.
    /// </summary>
    private const string PortFerme =
        "cannot connect to 192.168.1.16:45573: Aucune connexion n’a pu être établie car "
        + "l’ordinateur cible l’a expressément refusée. (10061)";

    private const string MachineAbsente =
        "cannot connect to 192.168.1.99:40000: Une tentative de connexion a échoué car le "
        + "parti connecté n’a pas répondu convenablement au-delà d’une certaine durée ou une "
        + "connexion établie a échoué car l’hôte de connexion n’a pas répondu. (10060)";

    private const string CleRefusee = "failed to connect to 192.168.1.16:37697";

    [Fact]
    public void Une_cle_refusee_se_reconnait()
    {
        // No network fault in the message: the connection
        // succeeded, and it is what came after that was refused.
        Assert.True(AdbConnectFailure.MeansRefusedKey(CleRefusee));
    }

    [Theory]
    [InlineData(PortFerme)]
    [InlineData(MachineAbsente)]
    public void Une_faute_reseau_n_est_pas_un_refus(string message)
    {
        // Advising a new pairing to someone who simply turned off
        // their phone would send them to the wrong page.
        Assert.False(AdbConnectFailure.MeansRefusedKey(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("already connected to 192.168.1.16:37697")]
    [InlineData("error: unknown host")]
    public void Ce_qu_on_ne_comprend_pas_ne_conclut_a_rien(string? message)
    {
        Assert.False(AdbConnectFailure.MeansRefusedKey(message));
    }

    [Fact]
    public void La_casse_et_les_espaces_ne_changent_rien()
    {
        Assert.True(AdbConnectFailure.MeansRefusedKey("  FAILED TO CONNECT TO 192.168.1.16:37697  "));
    }
}
