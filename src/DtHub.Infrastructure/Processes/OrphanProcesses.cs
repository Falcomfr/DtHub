using System.Diagnostics;

namespace DtHub.Infrastructure.Processes;

/// <summary>
/// Ramasse les processus laissés par une exécution précédente.
///
/// Une fin anormale de l'application laissait ses fenêtres de mirroring
/// ouvertes. Au lancement suivant elles n'étaient pas reconnues, et de
/// nouvelles venaient s'y ajouter : après quelques incidents, l'écran se
/// couvrait de fenêtres identiques.
/// </summary>
public static class OrphanProcesses
{
    /// <summary>
    /// Arrête les processus issus de l'exécutable donné. Seul le nôtre est
    /// visé, comparé par chemin complet : une copie de scrcpy installée par
    /// l'utilisateur ne doit jamais être touchée.
    /// </summary>
    /// <returns>Nombre de processus arrêtés.</returns>
    public static int KillFrom(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return 0;
        }

        var name = Path.GetFileNameWithoutExtension(executablePath);
        var full = Path.GetFullPath(executablePath);
        var killed = 0;

        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (process.Id == Environment.ProcessId || !IsOurs(process, full))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
            {
                // Le processus a pu disparaître entre-temps, ou appartenir à
                // une autre session. Rien à signaler.
                _ = exception;
            }
            finally
            {
                process.Dispose();
            }
        }

        return killed;
    }

    private static bool IsOurs(Process process, string executablePath)
    {
        try
        {
            return string.Equals(
                process.MainModule?.FileName, executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Le chemin d'un processus d'une autre session est illisible.
            // Dans le doute, on n'y touche pas.
            return false;
        }
    }
}
