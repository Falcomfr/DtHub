using System.IO;
using System.Reflection;

namespace DtHub.App.Windows;

/// <summary>
/// Le script qui lit la page de l'Almanax.
///
/// À part du pont des guides : celui-ci cadre une page pour l'afficher, et il
/// est écrit pour la structure d'un autre site. Ici on ne cadre rien, on lit
/// et la page n'est jamais montrée.
/// </summary>
internal static class AlmanaxBridge
{
    /// <summary>Le pont, tel qu'on l'injecte avant la création du document.</summary>
    public static string Script()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("DtHub.App.almanax-bridge.js")
            ?? throw new InvalidOperationException(
                "La ressource « DtHub.App.almanax-bridge.js » est absente de l'assembly.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
