using DtHub.Mac.Platform;

namespace DtHub.Tests.Mac;

/// <summary>
/// Linux's single instance (src/DtHub.Mac/Platform/SingleInstance.cs,
/// linked here): a second start wakes the first and opens nothing.
/// </summary>
public sealed class SingleInstanceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dthub-instance-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder that resists deletion should not fail a test.
        }
    }

    [Fact]
    public async Task Un_second_lancement_est_refuse_et_reveille_le_premier()
    {
        using var first = SingleInstance.TryClaim(_root);
        Assert.NotNull(first);

        var woken = new TaskCompletionSource();
        first.Woken += (_, _) => woken.TrySetResult();

        Assert.Null(SingleInstance.TryClaim(_root));

        Assert.Same(woken.Task, await Task.WhenAny(woken.Task, Task.Delay(TimeSpan.FromSeconds(10))));
    }

    [Fact]
    public void La_place_se_libere_quand_le_premier_s_arrete()
    {
        SingleInstance.TryClaim(_root)!.Dispose();

        using var next = SingleInstance.TryClaim(_root);

        Assert.NotNull(next);
    }
}
