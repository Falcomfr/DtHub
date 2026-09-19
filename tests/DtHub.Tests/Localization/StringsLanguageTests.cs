using System.Globalization;

using DtHub.Core.Localization;

// The language is one setting for the whole process, so a test that
// moves it cannot run beside one that reads it. The suite takes seven
// seconds; running it in order costs less than the flakiness would.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DtHub.Tests.Localization;

/// <summary>
/// Which language the text comes back in.
///
/// **Found on screen, not in a test.** A panel set to English showed
/// "Devices" and "Shortcuts" beside "Connecté en Wi-Fi" and "ouvert à
/// l'instant". The labels written in XAML were built while the chosen
/// culture was still on the thread; everything the device sweep
/// produced afterwards ran from a timer callback, outside that
/// execution context, and came back in Windows' language.
/// </summary>
public class StringsLanguageTests : IDisposable
{
    public void Dispose()
    {
        // Back to the thread's own, which the suite pins to French.
        Strings.Speak(null);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Sans_choix_la_langue_du_fil_s_applique()
    {
        Strings.Speak(null);

        Assert.Equal("Connecté en Wi-Fi", Strings.Get("ConnectedByWifi"));
    }

    /// <summary>
    /// The contract the fix rests on: what the application decided to
    /// speak wins, whichever thread happens to ask.
    /// </summary>
    [Fact]
    public void La_langue_choisie_l_emporte_sur_celle_du_fil()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Connected by Wi-Fi", Strings.Get("ConnectedByWifi"));
        Assert.Equal("just opened", Strings.Get("SessionJustOpened"));
    }

    [Fact]
    public void Le_choix_vaut_aussi_pour_les_textes_a_trous()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("en"));

        Assert.StartsWith("profile ", Strings.Format("ProfileOrigin", 0, "Main"), StringComparison.Ordinal);
    }

    [Fact]
    public void Le_choix_vaut_aussi_pour_les_textes_facultatifs()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Connected by Wi-Fi", Strings.Optional("ConnectedByWifi"));
    }
}
