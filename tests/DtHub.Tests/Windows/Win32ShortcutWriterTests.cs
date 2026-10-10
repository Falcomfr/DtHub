using DtHub.Infrastructure.Windows;

namespace DtHub.Tests.Windows;

public sealed class Win32ShortcutWriterTests : IDisposable
{
    private static readonly DateTime LongAgo = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-shortcut-" + Guid.NewGuid().ToString("N"));

    private readonly Win32ShortcutWriter _writer = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Ne_reecrit_pas_un_raccourci_qui_mene_deja_au_bon_endroit()
    {
        // A program that rewrites a shortcut to itself at every startup
        // is what Kaspersky's behavior detection expects of a trojan.
        var link = Path.Combine(_directory, "DT Hub.lnk");
        Assert.True(_writer.Write(link, @"C:\Jeux\DtHub.exe"));
        File.SetLastWriteTimeUtc(link, LongAgo);

        Assert.True(_writer.Write(link, @"C:\Jeux\DtHub.exe"));

        Assert.Equal(LongAgo, File.GetLastWriteTimeUtc(link));
    }

    [Fact]
    public void Reecrit_un_raccourci_qui_mene_a_un_autre_executable()
    {
        var link = Path.Combine(_directory, "DT Hub.lnk");
        Assert.True(_writer.Write(link, @"C:\Ancien\DtHub.exe"));
        File.SetLastWriteTimeUtc(link, LongAgo);

        Assert.True(_writer.Write(link, @"C:\Jeux\DtHub.exe"));

        Assert.NotEqual(LongAgo, File.GetLastWriteTimeUtc(link));
    }
}
