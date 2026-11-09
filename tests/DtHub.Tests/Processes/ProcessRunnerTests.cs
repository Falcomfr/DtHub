using System.Runtime.InteropServices;

using DtHub.Core.Processes;
using DtHub.Infrastructure.Processes;

namespace DtHub.Tests.Processes;

/// <summary>
/// Checks the real executor against a system shell. These tests
/// require no phone, no network, and no special privilege.
/// </summary>
public class ProcessRunnerTests
{
    private static readonly bool OnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static ProcessRequest Shell(string command, TimeSpan? timeout = null) =>
        new()
        {
            FileName = OnWindows ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : "/bin/sh",
            Arguments = OnWindows ? ["/c", command] : ["-c", command],
            Timeout = timeout,
        };

    [Fact]
    public async Task La_sortie_standard_est_capturee()
    {
        var result = await new ProcessRunner().RunAsync(Shell("echo bonjour"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("bonjour", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_sortie_d_erreur_est_capturee_separement()
    {
        var command = OnWindows ? "echo probleme 1>&2" : "echo probleme 1>&2";

        var result = await new ProcessRunner().RunAsync(Shell(command), CancellationToken.None);

        Assert.Contains("probleme", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("probleme", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_code_de_retour_non_nul_est_un_resultat_et_non_une_exception()
    {
        var result = await new ProcessRunner().RunAsync(Shell("exit 3"), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Un_executable_introuvable_leve_une_exception_explicite()
    {
        var request = new ProcessRequest
        {
            FileName = Path.Combine(Path.GetTempPath(), "dthub-executable-qui-n-existe-pas.exe"),
        };

        var exception = await Assert.ThrowsAsync<ProcessLaunchException>(
            () => new ProcessRunner().RunAsync(request, CancellationToken.None));

        Assert.Contains("n'a pas pu être démarré", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_processus_trop_long_est_tue_et_signale_comme_expire()
    {
        var command = OnWindows ? "ping -n 30 127.0.0.1 > nul" : "sleep 30";

        var result = await new ProcessRunner().RunAsync(
            Shell(command, TimeSpan.FromMilliseconds(600)), CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(result.Duration < TimeSpan.FromSeconds(15), $"Durée observée : {result.Duration}");
    }

    [Fact]
    public async Task Un_processus_expire_peut_emporter_ses_enfants()
    {
        // The child keeps the error pipe: killing the shell alone leaves the
        // runner waiting five seconds for it to close.
        var command = OnWindows ? "ping -n 30 127.0.0.1 > nul" : "sh -c 'sleep 30'";

        var result = await new ProcessRunner().RunAsync(
            Shell(command, TimeSpan.FromMilliseconds(600)) with { KillEntireTree = true }, CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.True(result.Duration < TimeSpan.FromSeconds(4), $"Durée observée : {result.Duration}");
    }

    [Fact]
    public async Task Une_annulation_de_l_appelant_remonte_en_exception()
    {
        var command = OnWindows ? "ping -n 30 127.0.0.1 > nul" : "sleep 30";
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessRunner().RunAsync(Shell(command), source.Token));
    }

    [Fact]
    public async Task Un_jeton_deja_annule_arrete_avant_tout_lancement()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessRunner().RunAsync(Shell("echo bonjour"), source.Token));
    }

    [Fact]
    public async Task Une_sortie_volumineuse_ne_bloque_pas_l_execution()
    {
        // The classic trap: the pipe buffer fills up and the child
        // process stays frozen if the caller has not started reading.
        var command = OnWindows
            ? "for /L %i in (1,1,2000) do @echo ligne-%i-remplissage-du-tampon"
            : "for i in $(seq 1 2000); do echo ligne-$i-remplissage-du-tampon; done";

        var result = await new ProcessRunner().RunAsync(
            Shell(command, TimeSpan.FromSeconds(60)), CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(2000, result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Un_argument_contenant_des_espaces_reste_un_seul_argument()
    {
        var request = new ProcessRequest
        {
            FileName = OnWindows ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : "/bin/sh",
            Arguments = OnWindows ? ["/c", "echo", "deux mots"] : ["-c", "echo \"$0\"", "deux mots"],
            Timeout = TimeSpan.FromSeconds(30),
        };

        var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);

        Assert.Contains("deux mots", result.StandardOutput, StringComparison.Ordinal);
    }

    // Called from the WPF thread, Process.Start held it 5 to 27 ms per adb
    // command, and the tabbed frame, which shares that thread, froze with it.
    [Fact]
    public void Le_lancement_ne_se_fait_pas_sur_le_fil_de_l_appelant()
    {
        var (caller, launch) = Threads((runner, request) => runner.RunAsync(request));

        Assert.NotEqual(caller, launch);
    }

    [Fact]
    public void Le_lancement_en_octets_ne_se_fait_pas_sur_le_fil_de_l_appelant()
    {
        var (caller, launch) = Threads((runner, request) => runner.RunForBytesAsync(request));

        Assert.NotEqual(caller, launch);
    }

    // A dedicated thread is never a pool thread: if the launch is prepared
    // anywhere but there, it was handed off.
    private static (int Caller, int? Launch) Threads(Func<ProcessRunner, ProcessRequest, Task> run)
    {
        var arguments = new ThreadRecordingArguments(OnWindows ? ["/c", "echo bonjour"] : ["-c", "echo bonjour"]);
        var request = Shell("") with { Arguments = arguments };
        var caller = 0;
        var thread = new Thread(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            run(new ProcessRunner(), request).GetAwaiter().GetResult();
        });

        thread.Start();
        thread.Join();

        Assert.NotNull(arguments.ThreadId);
        return (caller, arguments.ThreadId);
    }

    private sealed class ThreadRecordingArguments(string[] items) : IReadOnlyList<string>
    {
        public int? ThreadId { get; private set; }

        public int Count => items.Length;

        public string this[int index] => items[index];

        public IEnumerator<string> GetEnumerator()
        {
            ThreadId = Environment.CurrentManagedThreadId;
            return ((IEnumerable<string>)items).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void La_ligne_de_commande_lisible_protege_les_chemins_a_espaces()
    {
        var request = new ProcessRequest(@"C:\Program Files\adb.exe", "-s", "192.168.1.25:5555", "shell", "pm list users");

        Assert.Equal(
            "\"C:\\Program Files\\adb.exe\" -s 192.168.1.25:5555 shell \"pm list users\"",
            request.ToDisplayString());
    }
}
