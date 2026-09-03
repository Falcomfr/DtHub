using System.IO;
using System.Windows;

using DtHub.Core.Storage;

namespace DtHub.App.Services;

/// <summary>
/// Prépare l'icône que porteront les fenêtres de jeu.
///
/// scrcpy la lit dans un dossier désigné par une variable d'environnement, et
/// y cherche un fichier au nom fixe. L'image est extraite des ressources de
/// l'application : dans une publication en fichier unique, il n'y a pas de
/// fichier sur le disque à désigner.
/// </summary>
public static class WindowIcons
{
    private const string IconFile = "scrcpy.png";

    /// <summary>
    /// Dossier prêt à l'emploi, ou <c>null</c> si l'icône n'a pas pu être
    /// écrite. Les fenêtres gardent alors celle de scrcpy : ce n'est pas une
    /// raison de refuser de démarrer.
    /// </summary>
    public static string? EnsureDirectory(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            var directory = Path.Combine(paths.CacheDirectory, "icons");
            var target = Path.Combine(directory, IconFile);

            // Réécrit à chaque démarrage : garder la première copie figerait
            // l'ancienne image après un changement de marque.
            var source = Application.GetResourceStream(new Uri("assets/app.png", UriKind.Relative));

            if (source is null)
            {
                return null;
            }

            Directory.CreateDirectory(directory);

            using var stream = source.Stream;
            using var file = File.Create(target);
            stream.CopyTo(file);

            return directory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Silence assumé : l'icône est un confort. Sans elle la fenêtre
            // garde celle du système, et rien d'autre n'en dépend.
            return null;
        }
    }
}
