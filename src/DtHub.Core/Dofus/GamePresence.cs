namespace DtHub.Core.Dofus;

/// <summary>
/// Whether a phone carries the game, as far as we actually know.
///
/// **Three values, and the third one is the point.** Finding the
/// accounts asks each profile of each phone two questions and was
/// measured at 2.9 seconds, so the phones appear on screen before the
/// answer arrives. With two values, that gap had to be spelled as
/// "no game", which is a plain lie told several times a minute.
/// </summary>
public enum GamePresence
{
    /// <summary>Not asked yet, or asked and the answer has not come back.</summary>
    Unknown,

    /// <summary>Asked, and at least one profile carries the game.</summary>
    Present,

    /// <summary>Asked, and no profile carries the game.</summary>
    Absent,
}
