namespace DtHub.Tests;

/// <summary>
/// Le dossier du dépôt, retrouvé depuis celui où tournent les épreuves. Sert
/// aux contrôles qui portent sur le texte des fichiers plutôt que sur du code
/// exécuté.
/// </summary>
internal static class RepositoryRoot
{
    public static string Path()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(System.IO.Path.Combine(directory.FullName, "DtHub.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }
}
