using System.Runtime.InteropServices;

using DtHub.Core.Processes;
using DtHub.Infrastructure.Processes;

namespace DtHub.Tests.Processes;

public class ProcessSessionTests
{
    [Fact]
    public async Task Couper_tout_l_arbre_arrete_aussi_les_processus_enfants()
    {
        // The child inherits the output pipe: the output only ends once it
        // is gone too, thirty seconds later if it survived the shell.
        var onWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        await using var session = ProcessSession.Start(new ProcessRequest
        {
            FileName = onWindows ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : "/bin/sh",
            Arguments = onWindows ? ["/c", "ping -n 30 127.0.0.1"] : ["-c", "sh -c 'echo 127.0.0.1; sleep 30'"],
            KillEntireTree = true,
        });

        // A line from the child itself: killed before the shell starts it,
        // it would not be in the tree yet.
        while (!(await session.Output.ReadAsync()).Text.Contains("127.0.0.1", StringComparison.Ordinal))
        {
        }

        session.Kill();

        // Drained: the reader only completes once every line is read.
        var drained = Task.Run(async () =>
        {
            await foreach (var _ in session.Output.ReadAllAsync())
            {
            }
        });
        await drained.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
