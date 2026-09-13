namespace DtHub.Infrastructure.Updates;

/// <summary>
/// What the update targets: the version that is running and the
/// file that carries it.
///
/// Passed in rather than guessed at runtime: this is what makes
/// applying the update testable somewhere other than the machine of
/// whoever writes it.
/// </summary>
/// <param name="Running">The current version.</param>
/// <param name="ExecutablePath">
/// The full path of the current executable.
/// </param>
public sealed record UpdateTarget(Version Running, string ExecutablePath);
