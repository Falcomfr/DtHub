namespace DtHub.Core.Devices;

/// <summary>
/// Tells a phone whose game plays nothing any more, reopen or not.
///
/// **Reported on 2026-10-22.** A player on a Samsung lost the sound of both
/// his accounts, the capture for the PC still running: the game had no
/// active player at all. Relaunching DT Hub did nothing, only a restart of
/// the phone brought it back. The phone's media volume is not the cause:
/// at 0 on a Xiaomi, the PC still hears the game, the capture is taken
/// before it.
///
/// **Shown only while the sound goes to the PC.** A game silent by its own
/// options looks the same from here, so the text says to check them first.
/// A player who keeps the sound on the phone has nothing to miss on the PC,
/// and the log line is enough for him.
/// </summary>
public sealed class GameSilence
{
    /// <summary>
    /// Silent readings in a row before saying it, one every thirty seconds:
    /// a loading screen is silent too.
    /// </summary>
    public const int SilentReadings = 3;

    private readonly Lock _gate = new();

    private readonly Dictionary<string, int> _silent = new(StringComparer.Ordinal);

    /// <summary>Phones whose last reading found the sound going to the PC.</summary>
    private readonly HashSet<string> _carried = new(StringComparer.Ordinal);

    /// <summary>
    /// Takes a reading, and returns true on the one that finds the game
    /// silent, once per silence.
    /// </summary>
    /// <param name="carried">
    /// True when the sound of this phone goes to the PC: a live window
    /// carries it, or the phone's sound relay runs.
    /// </param>
    public bool Read(string serial, GameSound sound, bool carried)
    {
        ArgumentNullException.ThrowIfNull(sound);

        lock (_gate)
        {
            if (carried)
            {
                _ = _carried.Add(serial);
            }
            else
            {
                _ = _carried.Remove(serial);
            }

            if (!sound.IsSilent)
            {
                _ = _silent.Remove(serial);
                return false;
            }

            return (_silent[serial] = _silent.GetValueOrDefault(serial) + 1) == SilentReadings;
        }
    }

    /// <summary>True while this phone's game has been silent long enough.</summary>
    public bool Lasts(string serial)
    {
        lock (_gate)
        {
            return _silent.GetValueOrDefault(serial) >= SilentReadings;
        }
    }

    /// <summary>True while the player should be told: silent, and heard on the PC.</summary>
    public bool Shows(string serial)
    {
        lock (_gate)
        {
            return _silent.GetValueOrDefault(serial) >= SilentReadings && _carried.Contains(serial);
        }
    }
}
