using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class InputInjectionCheckTests
{
    [Fact]
    public void Un_silence_complet_vaut_acceptation()
    {
        // Mesuré sur le téléphone de référence, où la souris fonctionne :
        // `adb shell input keyevent 0` rend 0, sans une ligne sur aucune des
        // deux sorties.
        Assert.Equal(InputInjection.Works, InputInjectionCheck.Read(0, string.Empty, string.Empty));
        Assert.Equal(InputInjection.Works, InputInjectionCheck.Read(0, null, null));
        Assert.Equal(InputInjection.Works, InputInjectionCheck.Read(0, "  \n ", "\t"));
    }

    [Theory]
    [InlineData("java.lang.SecurityException: Injecting to another application requires INJECT_EVENTS permission")]
    [InlineData("Injecting input events requires the caller to have the INJECT_EVENTS permission")]
    [InlineData("java.lang.SecurityException: Not allowed to inject events")]
    [InlineData("Permission Denial: injecting input event from pid 1234 requires INJECT_EVENTS")]
    public void Un_refus_nomme_est_reconnu(string sortie)
    {
        Assert.Equal(InputInjection.Denied, InputInjectionCheck.Read(255, string.Empty, sortie));
    }

    [Fact]
    public void Le_refus_est_reconnu_sur_l_une_ou_l_autre_sortie()
    {
        const string refus = "SecurityException: INJECT_EVENTS permission";

        Assert.Equal(InputInjection.Denied, InputInjectionCheck.Read(255, refus, string.Empty));
        Assert.Equal(InputInjection.Denied, InputInjectionCheck.Read(0, string.Empty, refus));
    }

    [Fact]
    public void Un_refus_generique_sans_rapport_avec_l_entree_ne_conclut_pas()
    {
        // Le shell rend la même famille d'erreur pour un dossier sécurisé
        // verrouillé. Ce n'est pas la souris, et le dire le serait à tort.
        Assert.Equal(
            InputInjection.Unknown,
            InputInjectionCheck.Read(
                255, string.Empty, "java.lang.SecurityException: Shell does not have permission to access user 150"));
    }

    [Fact]
    public void Une_sortie_inattendue_ne_vaut_pas_acceptation()
    {
        Assert.Equal(InputInjection.Unknown, InputInjectionCheck.Read(0, "Killed", string.Empty));
        Assert.Equal(InputInjection.Unknown, InputInjectionCheck.Read(1, string.Empty, string.Empty));
    }

    [Fact]
    public void La_sonde_envoie_la_touche_inconnue()
    {
        // Elle ne déclenche rien nulle part : c'est ce qui rend la question
        // posable sans agir sur l'appareil.
        Assert.Equal(["shell", "input", "keyevent", "0"], InputInjectionCheck.ProbeCommand);
    }
}
