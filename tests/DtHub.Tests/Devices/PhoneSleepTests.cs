using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class PhoneSleepTests
{
    /// <summary>
    /// Recorded on the 13T Pro, <c>dumpsys power | grep mWakefulness=</c>,
    /// the phone asleep with a game window open.
    /// </summary>
    private const string Endormi = "  mWakefulness=Dozing\n";

    [Fact]
    public void Un_telephone_en_veille_se_lit_endormi() =>
        Assert.True(PhoneSleep.IsAsleep(Endormi));

    [Fact]
    public void Un_telephone_eteint_se_lit_endormi() =>
        Assert.True(PhoneSleep.IsAsleep(Endormi.Replace("Dozing", "Asleep", StringComparison.Ordinal)));

    [Theory]
    [InlineData("Awake")]
    [InlineData("Dreaming")]
    public void Un_telephone_eveille_ou_en_economiseur_ne_dort_pas(string etat) =>
        Assert.False(PhoneSleep.IsAsleep(Endormi.Replace("Dozing", etat, StringComparison.Ordinal)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Can't find service: power\n")]
    public void Une_reponse_sans_l_etat_ne_dit_rien(string? reponse) =>
        Assert.Null(PhoneSleep.IsAsleep(reponse));

    [Theory]
    [InlineData(34, true)]
    [InlineData(33, true)]
    [InlineData(35, false)]
    [InlineData(36, false)]
    [InlineData(null, false)]
    public void Seul_un_android_14_ou_plus_ancien_ignore_les_clics_en_veille(int? sdk, bool concerne) =>
        Assert.Equal(concerne, PhoneSleep.DropsClicksWhenAsleep(sdk));
}
