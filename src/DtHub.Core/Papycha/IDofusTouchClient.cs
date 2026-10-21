using System.Text.Json;

namespace DtHub.Core.Papycha;

/// <summary>
/// Reads DOFUS Touch's own data server, the one the game itself asks for
/// its quest, dungeon and text tables. Its names are the ones the player
/// sees in game, in the language of the game.
/// </summary>
public interface IDofusTouchClient
{
    /// <summary>
    /// Reads rows of a data class (for example <c>Quests</c>), by id, or
    /// every row when <paramref name="ids"/> is null. Throws on a refusal
    /// or an unreadable answer: the callers catch.
    /// </summary>
    Task<IReadOnlyDictionary<int, JsonElement>> GetAsync(
        string dataClass,
        IReadOnlyCollection<int>? ids,
        string language,
        CancellationToken cancellationToken = default);

    /// <summary>Reads text rows by id. Throws like <see cref="GetAsync"/>.</summary>
    Task<IReadOnlyDictionary<int, string>> GetTextsAsync(
        IReadOnlyCollection<int> ids,
        string language,
        CancellationToken cancellationToken = default);
}
