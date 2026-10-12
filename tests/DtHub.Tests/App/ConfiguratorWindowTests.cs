namespace DtHub.Tests.App;

/// <summary>
/// The panel has no title bar of its own: once maximized, nothing
/// brings it back. A player dragged it against the top of the screen,
/// Windows maximized it, and the saved place reopened it maximized at
/// every launch.
/// </summary>
public sealed class ConfiguratorWindowTests
{
    [Fact]
    public void Le_panneau_ne_peut_etre_ni_agrandi_ni_ancre_par_Windows()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot.Path(), "src", "DtHub.App", "Windows", "ConfiguratorWindow.xaml"));

        // CanMinimize drops the resizing frame and the maximize box,
        // and with them the drag to the screen's edge and Win + arrow.
        Assert.Contains("ResizeMode=\"CanMinimize\"", xaml, StringComparison.Ordinal);
    }
}
