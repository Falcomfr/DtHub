namespace DtHub.Core.Papycha;

/// <summary>
/// A non-archived character of the player's tracker. <c>Gender</c>, <c>ClassKey</c> and <c>Color</c> are null when the
/// page did not give them: never guessed, since saving the level sends them back.
/// </summary>
public sealed record TrackerCharacter(string Id, string Name, int Level, string? Gender = null, string? ClassKey = null, string? Color = null);

/// <summary>What the tracker knows about one character's quests.</summary>
/// <param name="Statuses">Status per quest id, only the quests the player touched.</param>
/// <param name="Completed">Quests done, inferred ones included.</param>
/// <param name="Available">Quests the character can start.</param>
/// <param name="Next">Quests the site suggests next, in its order.</param>
public sealed record TrackerProgress(
    IReadOnlyDictionary<int, QuestStatus> Statuses,
    IReadOnlySet<int> Completed,
    IReadOnlySet<int> Available,
    IReadOnlyList<int> Next);

/// <summary>The linked player's tracker, as the page embeds it.</summary>
/// <param name="Nonce">WordPress nonce the page gives for its AJAX calls.</param>
public sealed record TrackerState(
    string Nonce,
    IReadOnlyList<TrackerCharacter> Characters,
    string? ActiveCharacterId,
    IReadOnlyDictionary<string, TrackerProgress> Progress);

/// <summary>Why a tracker page did or did not give a state.</summary>
public enum TrackerReadOutcome
{
    Linked,
    NotLinked,
    Unreadable,
}

public sealed record TrackerReadResult(TrackerReadOutcome Outcome, TrackerState? State);

/// <summary>Where the link with the player's Papycha tracker stands.</summary>
public enum TrackerLink
{
    /// <summary>Not read yet.</summary>
    Unknown,

    /// <summary>No tracker cookie: the player never linked, or unlinked.</summary>
    NotLinked,

    Linked,

    /// <summary>The cookie is there but the site no longer accepts it (phrase renewed).</summary>
    Lost,

    /// <summary>Network down, site changed, or the cookie could not be read.</summary>
    Unreadable,
}
