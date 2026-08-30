using DtHub.Core;
using DtHub.Core.Storage;

namespace DtHub.Infrastructure.Storage;

/// <summary>
/// Implémentation ancrée sur <c>%LOCALAPPDATA%</c>. La racine est
/// paramétrable pour que les tests écrivent dans un dossier temporaire.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductInfo.Slug);

        SettingsFile = Path.Combine(Root, "settings.json");
        DevicesFile = Path.Combine(Root, "devices.json");
        ProfilesFile = Path.Combine(Root, "profiles.json");
        CacheDirectory = Path.Combine(Root, "cache");
        QuestCatalogFile = Path.Combine(CacheDirectory, "papycha-quetes.json");
        LogsDirectory = Path.Combine(Root, "logs");
        ToolsDirectory = Path.Combine(Root, "tools");
    }

    public string Root { get; }
    public string SettingsFile { get; }
    public string DevicesFile { get; }
    public string ProfilesFile { get; }
    public string CacheDirectory { get; }

    public string QuestCatalogFile { get; }
    public string LogsDirectory { get; }
    public string ToolsDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ToolsDirectory);
    }
}
