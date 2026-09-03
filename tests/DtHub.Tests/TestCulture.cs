using System.Globalization;
using System.Runtime.CompilerServices;

namespace DtHub.Tests;

/// <summary>
/// Fixe la langue de toute la suite d'épreuves au français.
///
/// Depuis que l'application parle trois langues, les libellés viennent des
/// ressources : une épreuve qui attend « Ctrl + Maj + Tab » ou « Échap »
/// dépendait alors de la langue de la machine, passait sur un Windows français
/// et serait tombée sur un Windows anglais. La suite éprouve la formulation
/// française, et le dit ici une fois pour toutes.
///
/// Le pays est fixé de même, et pour la même raison : le format des nombres
/// est un réglage distinct de la langue, et « 8,4 Mb/s » devient « 8.4 Mb/s »
/// sur une machine anglaise.
///
/// Les épreuves qui portent sur le choix de la langue lui-même posent leur
/// propre culture et la rendent ensuite.
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
