using DtHub.Core.Storage;

namespace DtHub.Tests.Fakes;

/// <summary>Des chemins posés dans un dossier jetable.</summary>
public sealed class FakeAppPaths : IAppPaths
{
    public FakeAppPaths(string root)
    {
        Root = root;
        SettingsFile = Path.Combine(root, "settings.json");
        DevicesFile = Path.Combine(root, "devices.json");
        ProfilesFile = Path.Combine(root, "profiles.json");
        CacheDirectory = Path.Combine(root, "cache");
        QuestCatalogFile = Path.Combine(CacheDirectory, "quetes.json");
        LogsDirectory = Path.Combine(root, "logs");
        ToolsDirectory = Path.Combine(root, "tools");
        UpdatesDirectory = Path.Combine(root, "updates");
    }

    public string Root { get; }
    public string SettingsFile { get; }
    public string DevicesFile { get; }
    public string ProfilesFile { get; }
    public string CacheDirectory { get; }
    public string QuestCatalogFile { get; }
    public string LogsDirectory { get; }
    public string ToolsDirectory { get; }
    public string UpdatesDirectory { get; }

    public void EnsureCreated()
    {
        _ = Directory.CreateDirectory(Root);
        _ = Directory.CreateDirectory(CacheDirectory);
        _ = Directory.CreateDirectory(LogsDirectory);
        _ = Directory.CreateDirectory(ToolsDirectory);
        _ = Directory.CreateDirectory(UpdatesDirectory);
    }
}
