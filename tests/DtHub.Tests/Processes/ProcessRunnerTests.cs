using System.Runtime.InteropServices;

using DtHub.Core.Processes;
using DtHub.Infrastructure.Processes;

namespace DtHub.Tests.Processes;

/// <summary>
/// Vérifie l'exécuteur réel sur un interpréteur système. Ces tests ne
/// requièrent ni téléphone, ni réseau, ni privilège particulier.
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
        // Le piège classique : le tampon du tube se remplit et le processus fils
        // reste figé si l'appelant n'a pas commencé à lire.
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

    [Fact]
    public void La_ligne_de_commande_lisible_protege_les_chemins_a_espaces()
    {
        var request = new ProcessRequest(@"C:\Program Files\adb.exe", "-s", "192.168.1.25:5555", "shell", "pm list users");

        Assert.Equal(
            "\"C:\\Program Files\\adb.exe\" -s 192.168.1.25:5555 shell \"pm list users\"",
            request.ToDisplayString());
    }
}
