using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DtHub.App.Services;

/// <summary>
/// Pose la barre de titre sombre sur les fenêtres qui gardent celle de Windows.
///
/// L'application n'a qu'une palette, sombre. Le configurateur et les fenêtres
/// de guides se dessinent leur propre châssis, mais les huit boîtes de dialogue
/// gardaient celui du système, qui se rend en clair : un bandeau pâle au-dessus
/// d'un corps noir, sur la moitié des fenêtres du produit.
///
/// L'attribut est posé une fois pour toutes par un gestionnaire de classe, et
/// non fenêtre par fenêtre : celles qu'on écrira plus tard l'auront sans que
/// personne ait à y penser. Les fenêtres à châssis propre n'ont pas de barre à
/// teindre, l'appel ne leur coûte rien et ne leur fait rien.
/// </summary>
internal static class DarkTitleBar
{
    /// <summary>
    /// Le numéro d'attribut a changé en cours de route : 19 sur les Windows 10
    /// d'avant la version 2004, 20 depuis. Le socle déclaré descend à 1809, les
    /// deux sont donc essayés. Un numéro inconnu rend une erreur que l'on
    /// ignore, ce qui est le bon comportement : la fenêtre reste claire.
    /// </summary>
    private const int DarkModeBefore20H1 = 19;
    private const int DarkMode = 20;

    /// <summary>Branche la teinture sur toutes les fenêtres de l'application.</summary>
    public static void Arm() =>
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply(sender as Window)));

    private static void Apply(Window? window)
    {
        if (window is null)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var on = 1;

        if (DwmSetWindowAttribute(handle, DarkMode, ref on, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, DarkModeBefore20H1, ref on, sizeof(int));
        }
    }

    // DllImport et non LibraryImport : ce dernier exige du code non sécurisé
    // pour un paramètre passé par référence, et le projet ne l'autorise pas.
    // C'est aussi la forme qu'emploient les quarante et un autres appels du
    // dépôt.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window, int attribute, ref int value, int size);
}
