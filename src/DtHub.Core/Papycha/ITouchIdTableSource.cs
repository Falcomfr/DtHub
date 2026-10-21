namespace DtHub.Core.Papycha;

/// <summary>
/// Where the identifier table comes from. The table is DT Hub's own work:
/// it holds numbers, never a word of the game's text.
/// </summary>
public interface ITouchIdTableSource
{
    /// <summary>
    /// A new table on each call. Empty if it cannot be read.
    /// </summary>
    TouchIdTable Load();
}
