using System.Globalization;
using System.Runtime.CompilerServices;

namespace DtHub.Tests;

/// <summary>
/// Pins the language of the entire test suite to French.
///
/// Since the application speaks three languages, the labels come from
/// resources: a test expecting "Ctrl + Maj + Tab" or "Échap" would
/// then depend on the machine's language, passing on a French Windows
/// and failing on an English one. The suite tests the French wording,
/// and states it here once and for all.
///
/// The country is pinned the same way, and for the same reason:
/// number formatting is a setting distinct from the language, and
/// "8,4 Mb/s" becomes "8.4 Mb/s" on an English machine.
///
/// Tests that concern the choice of language itself set their own
/// culture and then restore it.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Fixer()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    }
}
