using System.ComponentModel;
using System.Diagnostics;

namespace DtHub.Lanceur;

/// <summary>
/// Opens DT Hub after making sure the published binary matches the code.
///
/// This is what the desktop shortcut targets. It used to target
/// <c>lancer.cmd</c>, and before that the binary itself. Each of those
/// two had one fault, and this project is the one that has neither.
///
/// Targeting the binary guaranteed nothing: it dated from the last
/// publish, not from the last edit, and you could play for hours on a
/// stale version without noticing. Measured on 2026-09-13: the shortcut
/// was pointing at a hand built copy from an update trial, three days
/// behind the code, and nothing on screen said so.
///
/// Targeting the batch file fixed that and cost a console. Windows has
/// to open one to interpret a .cmd, and a shortcut's "minimised" only
/// decides how that window shows, not whether it exists.
///
/// No step here is blocking. SDK missing, publish failed, file locked by
/// an instance already open: it opens whatever is there anyway. Better
/// yesterday's version than no application at all.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Beyond this, the republish is given up and the binary present is
    /// opened. The republish is incremental: 1.1 s when nothing changed,
    /// 10.6 s after an edit. Two minutes is no longer a wait, it is a
    /// fault, and a fault must not keep the application shut.
    /// </summary>
    private static readonly TimeSpan PublishBudget = TimeSpan.FromMinutes(2);

    private static int Main()
    {
        var root = RepositoryRoot();
        var output = Path.Combine(root, "build", "publish");
        var binary = Path.Combine(output, "DtHub.exe");
        var log = Path.Combine(root, "build", "publication.log");

        Republish(root, output, log);

        if (!File.Exists(binary))
        {
            // Nothing to open, and no console of our own to say it in.
            // The batch file holds that message and waits on it, which
            // is the only way it gets read.
            return OpenTheBatchFileSoItCanExplain(root);
        }

        using var application = Process.Start(new ProcessStartInfo(binary)
        {
            WorkingDirectory = output,
            UseShellExecute = true,
        });

        return 0;
    }

    /// <summary>
    /// Climbs to the folder holding the solution.
    ///
    /// Published, this executable sits in <c>build/</c> and the answer is
    /// one level up. Run through <c>dotnet run</c> it sits under
    /// <c>bin/</c>, several levels down, which is why this looks for a
    /// landmark rather than counting folders.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DtHub.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    }

    /// <summary>
    /// Rebuilds the shipped file, without a window and without blocking.
    ///
    /// The publish options are not repeated here: they live in
    /// <c>win-x64.pubxml</c>, which is their single source.
    /// </summary>
    private static void Republish(string root, string output, string log)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[]
                 {
                     "publish",
                     Path.Combine("src", "DtHub.App"),
                     "-p:PublishProfile=win-x64",
                     "-o", output,
                     "--nologo",
                     "-v", "q",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var publish = Process.Start(start);

            if (publish is null)
            {
                return;
            }

            // Both streams are drained while the process runs. Waiting
            // first and reading after deadlocks as soon as the output
            // fills the pipe, which a failed publish does easily.
            var standardOutput = publish.StandardOutput.ReadToEndAsync();
            var standardError = publish.StandardError.ReadToEndAsync();

            if (!publish.WaitForExit((int)PublishBudget.TotalMilliseconds))
            {
                publish.Kill(entireProcessTree: true);
                Write(log, "Publication abandonnée : elle a dépassé son délai.");
                return;
            }

            Write(log, standardOutput.Result + standardError.Result);
        }
        catch (Exception failure) when (failure
                                            is Win32Exception
                                            or IOException
                                            or UnauthorizedAccessException
                                            or InvalidOperationException
                                            or AggregateException)
        {
            // No SDK on the machine, log not writable, binary held open by
            // an instance already running. None of these is a reason to
            // keep the application shut, and the log says what happened.
            Write(log, "Publication impossible : " + failure.Message);
        }
    }

    /// <summary>
    /// Writes the report of the republish, and gives up silently if even
    /// that is refused. There is nowhere left to complain to.
    /// </summary>
    private static void Write(string log, string content)
    {
        try
        {
            File.WriteAllText(log, content);
        }
        catch (Exception refusal) when (refusal is IOException or UnauthorizedAccessException)
        {
            // Deliberate silence: the caller is about to open the
            // application, which matters more than this report.
        }
    }

    /// <summary>
    /// Last resort: no binary to open. The batch file prints the reason
    /// and waits, in the console that Windows gives it.
    /// </summary>
    private static int OpenTheBatchFileSoItCanExplain(string root)
    {
        var batch = Path.Combine(root, "build", "lancer.cmd");

        if (!File.Exists(batch))
        {
            return 1;
        }

        using var explanation = Process.Start(new ProcessStartInfo(batch)
        {
            WorkingDirectory = Path.Combine(root, "build"),
            UseShellExecute = true,
        });

        return 1;
    }
}
