namespace DtHub.Core.Papycha;

/// <summary>Outcome of a status write on the tracker.</summary>
/// <param name="Message">Why the site refused, when it said.</param>
/// <param name="After">
/// Outcome of the re-read done when a retry happened; <c>NotLinked</c>
/// means the access phrase was renewed and the cookie is dead.
/// </param>
public sealed record TrackerWriteResult(bool Success, string? Message, TrackerReadOutcome? After = null);

/// <summary>The player's quest tracker on papycha.fr, read and written with their cookie.</summary>
public interface IPapychaTracker
{
    Task<TrackerReadResult> ReadAsync(string cookie, CancellationToken cancellationToken = default);

    Task<TrackerWriteResult> SetStatusAsync(
        string cookie,
        string characterId,
        int questId,
        QuestStatus status,
        CancellationToken cancellationToken = default);

    Task<TrackerWriteResult> SaveCharacterAsync(
        string cookie,
        TrackerCharacter character,
        CancellationToken cancellationToken = default);
}
