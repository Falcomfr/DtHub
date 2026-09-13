using DtHub.Core.Processes;

namespace DtHub.Core.Adb;

/// <summary>
/// ADB surface used by the rest of the application. Everything goes
/// through here, which makes it possible to replay any scenario in
/// tests without hardware.
/// </summary>
public interface IAdbClient
{
    /// <summary>Starts the ADB server if it is not already running.</summary>
    Task StartServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the ADB server. Only call this on the user's explicit
    /// request: the server is shared with the machine's other
    /// tools, and stopping it would cut them off too.
    /// </summary>
    Task StopServerAsync(CancellationToken cancellationToken = default);

    /// <summary>ADB version, for the diagnostics page.</summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the devices seen by the ADB server.</summary>
    /// <param name="detailed">Adds model, product and USB path.</param>
    Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(
        bool detailed = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a raw ADB command. <paramref name="serial"/> targets
    /// a specific device; <c>null</c> targets the server.
    /// </summary>
    /// <param name="sensitiveValues">
    /// Values to mask in the logs, for example a pairing code.
    /// </param>
    Task<ProcessResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        IReadOnlyCollection<string>? sensitiveValues = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command in the device's shell and returns the
    /// output.
    /// </summary>
    /// <exception cref="AdbException">The command failed.</exception>
    Task<string> ShellAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the device's system properties via <c>getprop</c>.
    /// </summary>
    /// <summary>
    /// Executes a command in the device's shell and returns its
    /// output byte for byte.
    ///
    /// This is ADB's binary variant. Unlike "shell", "exec-out"
    /// does not allocate a pseudo-terminal and therefore rewrites
    /// no line ending: that is what lets an intact image be
    /// extracted from it.
    ///
    /// Does not throw on a non-zero exit code and does not classify
    /// the output: the error interpreter reads text, and an image
    /// is not text. The caller checks for itself what it receives.
    /// </summary>
    Task<ProcessBytes> ExecOutAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(
        string serial,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pairs the PC with a phone over wireless debugging. The code
    /// is neither logged nor kept after the call.
    /// </summary>
    Task<AdbPairResult> PairAsync(
        string host,
        int pairingPort,
        string pairingCode,
        CancellationToken cancellationToken = default);

    /// <summary>Establishes a wireless ADB connection to an address.</summary>
    Task<AdbConnectResult> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects a wireless connection. <paramref name="address"/>
    /// set to <c>null</c> disconnects all of the ADB server's
    /// wireless connections.
    /// </summary>
    Task DisconnectAsync(string? address = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the wireless debugging announcements seen on the local
    /// network.
    /// </summary>
    Task<IReadOnlyList<MdnsService>> ListMdnsServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Waits for a device to become ready.</summary>
    /// <returns>
    /// True if the device is ready before the timeout expires.
    /// </returns>
    Task<bool> WaitForDeviceAsync(
        string serial,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
