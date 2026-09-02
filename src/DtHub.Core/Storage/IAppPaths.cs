namespace DtHub.Core.Storage;

/// <summary>
/// Emplacements des données de l'utilisateur. Tout est regroupé sous un seul
/// dossier pour qu'une désinstallation propre reste simple à expliquer.
/// </summary>
public interface IAppPaths
{
    /// <summary><c>%LOCALAPPDATA%\&lt;Slug&gt;</c>.</summary>
    string Root { get; }

    string SettingsFile { get; }
    string DevicesFile { get; }
    string ProfilesFile { get; }

    /// <summary>Métadonnées et icônes d'applications mises en cache.</summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Catalogue des quêtes de papycha. Rangé dans le cache et non près des
    /// réglages : ce n'est pas un choix de l'utilisateur, et le perdre ne coûte
    /// qu'une réindexation.
    /// </summary>
    string QuestCatalogFile { get; }

    /// <summary>Journaux avec rotation.</summary>
    string LogsDirectory { get; }

    /// <summary>Composants tiers téléchargés, un sous-dossier par version.</summary>
    string ToolsDirectory { get; }

    /// <summary>
    /// Mises à jour téléchargées, en attente d'être posées à l'arrêt. Dans le
    /// dossier de l'utilisateur et non près de l'exécutable : c'est le seul
    /// endroit où écrire ne demande aucun droit particulier.
    /// </summary>
    string UpdatesDirectory { get; }

    /// <summary>Crée les dossiers manquants. Idempotent.</summary>
    void EnsureCreated();
}
