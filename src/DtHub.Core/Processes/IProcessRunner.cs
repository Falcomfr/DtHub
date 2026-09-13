namespace DtHub.Core.Processes;

/// <summary>
/// Single entry point for launching an external process. The
/// entire ADB and scrcpy layer goes through this interface, which
/// allows simulating output in tests without a single phone.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs the process and waits for it to finish. Does not throw
    /// on a nonzero exit code: that is a result, not a technical
    /// error.
    /// </summary>
    /// <exception cref="ProcessLaunchException">
    /// The process could not start.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation requested by the caller.
    /// </exception>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the process and returns its standard output as-is, in
    /// bytes.
    ///
    /// A distinct path, not an option of the previous one: that one
    /// decodes UTF-8 and splits into lines, which mutilates an
    /// image. It serves a single need, extracting a file from an
    /// archive on the device, and there is no reason to broaden it
    /// without cause.
    /// </summary>
    /// <exception cref="ProcessLaunchException">
    /// The process could not start.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation requested by the caller.
    /// </exception>
    Task<ProcessBytes> RunForBytesAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default);
}
