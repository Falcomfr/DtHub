namespace DtHub.Core;

/// <summary>
/// Product identity. Single point of renaming on the code side.
/// The MSBuild counterpart is in Directory.Build.props (see AGENTS.md).
/// </summary>
public static class ProductInfo
{
    /// <summary>
    /// Name shown in the interface, the installer and the windows.
    /// </summary>
    public const string Name = "DT Hub";

    /// <summary>
    /// Technical identifier without spaces: name of the data folder
    /// and of the executable. Deliberately kept separate from the
    /// displayed name, so that a name change does not move the
    /// user's settings.
    /// </summary>
    public const string Slug = "DtHub";

    /// <summary>Public repository, used by the update check.</summary>
    public const string RepositoryUrl = "https://github.com/Falcomfr/DtHub";

    /// <summary>Displayed version, fed by the assembly at runtime.</summary>
    public static string Version { get; } =
        typeof(ProductInfo).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Select(a => a.InformationalVersion.Split('+')[0])
            .FirstOrDefault() ?? "0.0.0";
}
