using DtHub.Infrastructure.Storage;
using DtHub.Mac;

namespace DtHub.Tests.Mac;

/// <summary>
/// The Mac and Linux composition (src/DtHub.Mac/MacServices.cs) builds with
/// every constructor dependency registered: a service the shared launcher
/// or a shared view model starts taking crashed both at startup in 0.14.3.
/// Run on Windows and Linux for the shared graph, on the Mac CI for the
/// Mac's own registrations.
/// </summary>
public sealed class MacServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dthub-mac-services-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Serilog.Log.CloseAndFlush();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Chaque_service_du_Mac_trouve_toutes_ses_dependances()
    {
        // ValidateOnBuild throws here for a missing registration, as it did at launch.
        using var services = MacServices.Build(new AppPaths(_root));

        Assert.NotNull(services);
    }
}
