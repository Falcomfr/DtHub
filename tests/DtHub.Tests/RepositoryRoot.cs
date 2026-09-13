namespace DtHub.Tests;

/// <summary>
/// The repository's folder, found starting from the one where the
/// tests run. Used by checks that look at file text rather than
/// executed code.
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
