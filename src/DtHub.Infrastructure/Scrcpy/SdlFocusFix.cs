using System.Security.Cryptography;

using DtHub.Core.Processes;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Scrcpy;

/// <summary>
/// Puts DT Hub's patched SDL3.dll in place of the one scrcpy ships.
///
/// **The fault, measured on 2026-10-04.** In a tabbed frame, é, à, ? and
/// every other non-letter key typed nothing, while letters went through.
/// scrcpy sends letters as key events and everything else as text, and SDL
/// drops text unless it believes its window has the keyboard focus, which
/// it grants only to the foreground window. A docked window is a child of
/// the frame and never the foreground window. See
/// <c>third_party/sdl/MODIFICATIONS.md</c>.
///
/// Only scrcpy 4.1's own SDL3.dll is replaced, recognised by its digest: a
/// later scrcpy carries another SDL, and an older one put over it would
/// break it. And the original comes back if scrcpy no longer starts with
/// ours: no window at all would be worse than no accents in tabs.
/// </summary>
public sealed partial class SdlFocusFix
{
    /// <summary>SDL3.dll as shipped in scrcpy-win64-v4.1.zip.</summary>
    public const string OriginalSha256 = "0619eb2da6032984dc6e2098897aeacdbd66b0415bb87bc03e628159ba60b15d";

    private const string ResourceName = "DtHub.Infrastructure.SDL3.dll";

    private readonly string _originalSha256;
    private readonly byte[] _fixedDll;
    private readonly IProcessRunner _runner;
    private readonly ILogger<SdlFocusFix> _logger;

    public SdlFocusFix(IProcessRunner runner, ILogger<SdlFocusFix> logger)
        : this(OriginalSha256, ReadFixedDll(), runner, logger)
    {
    }

    public SdlFocusFix(string originalSha256, byte[] fixedDll, IProcessRunner runner, ILogger<SdlFocusFix> logger)
    {
        _originalSha256 = originalSha256;
        _fixedDll = fixedDll;
        _runner = runner;
        _logger = logger;
    }

    public static byte[] ReadFixedDll()
    {
        using var stream = typeof(SdlFocusFix).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Ressource « {ResourceName} » absente : third_party/sdl/SDL3.dll n'est pas embarqué.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    /// <param name="scrcpyPath">scrcpy.exe, next to which SDL3.dll lives.</param>
    public async Task ApplyAsync(string scrcpyPath, CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(Path.GetDirectoryName(scrcpyPath) ?? string.Empty, "SDL3.dll");

        try
        {
            if (!File.Exists(target))
            {
                return;
            }

            var original = await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false);
            if (Convert.ToHexStringLower(SHA256.HashData(original)) != _originalSha256)
            {
                return;
            }

            Replace(target, _fixedDll);

            // "--version" loads SDL3.dll and prints its version: what fails
            // here is what would keep every window from opening.
            if (await StartsAsync(scrcpyPath, cancellationToken).ConfigureAwait(false))
            {
                LogApplied(target);
                return;
            }

            // ponytail: retried on every launch, a run of scrcpy --version each time; remember the refusal if that ever shows.
            Replace(target, original);
            LogRolledBack(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A scrcpy left running holds the file. Text input stays broken
            // in tabs until the next launch, which tries again.
            LogFailed(target, exception.Message);
        }
    }

    private async Task<bool> StartsAsync(string scrcpyPath, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner
                .RunAsync(
                    new ProcessRequest(scrcpyPath, "--version") { Timeout = TimeSpan.FromSeconds(10) },
                    cancellationToken)
                .ConfigureAwait(false);

            return result.Succeeded && result.StandardOutput.Contains("SDL:", StringComparison.Ordinal);
        }
        catch (ProcessLaunchException)
        {
            // Rolled back and logged by the caller.
            return false;
        }
    }

    /// <summary>
    /// Written beside then swapped: an interrupted write must not leave a
    /// broken SDL3.dll that the digest would never match again.
    /// </summary>
    private static void Replace(string target, byte[] content)
    {
        var staging = target + ".partiel";
        File.WriteAllBytes(staging, content);
        File.Move(staging, target, overwrite: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SDL3.dll corrigé posé dans {path}.")]
    private partial void LogApplied(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "scrcpy ne démarre pas avec le SDL3.dll corrigé, original remis dans {path}.")]
    private partial void LogRolledBack(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SDL3.dll corrigé non posé dans {path} : {reason}")]
    private partial void LogFailed(string path, string reason);
}
