namespace DtHub.Core.Devices;

/// <summary>
/// Tells a phone whose games played before the windows reopened and play
/// nothing after.
///
/// **Reported on 2026-10-18.** A player on a Galaxy S25+ unticked the sharp
/// image then chose the High quality: two reopens in a row, each stopping
/// and relaunching his four games. They came back silent, the capture still
/// running, on the PC and on the phone alike, and only a restart of the
/// phone brought the sound back. On a Xiaomi 13T Pro the same reopens leave
/// the sound alone.
///
/// **Only after a reopen, only where the game played.** A silent game alone
/// says nothing: its own options keep it quiet too. Heard just before the
/// reopen and not once in a minute after, it is the reopen. A relaunched game
/// is not quiet on its login screen: on the 13T Pro its players were active
/// again within fifteen seconds of every reopen, before anyone logged in.
/// </summary>
public sealed class SoundAfterReopen
{
    /// <summary>
    /// Silent readings in a row before saying it, one every thirty seconds:
    /// a relaunched game takes some seconds to play again.
    /// </summary>
    public const int SilentReadings = 3;

    /// <summary>
    /// How long a reopen stays the reason. Past it, the windows may have
    /// closed and opened again on accounts whose sound is off.
    /// </summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    /// <summary>
    /// What the phone says of its sound when it is found stuck, written to
    /// the log as is: the scrcpy servers still running, which outlive a
    /// closed window and die with a restart; every player and capture with
    /// its state; Android's own mutes; who holds the audio focus; the media
    /// stream's mute and volume. Written too when the game falls silent
    /// without a reopen, see <see cref="GameSilence" />.
    /// </summary>
    public const string Probe =
        "echo serveurs; for p in $(pgrep -f com.genymobile.scrcpy.Server); do tr '\\0' ' ' < /proc/$p/cmdline | grep -oE 'scid=[0-9a-f]+'; done; "
        + "echo lecteurs; dumpsys media.audio_policy | grep -E 'uid [0-9]+; State:' | head -40; "
        + "echo durcissement; dumpsys audio | grep -i hardening | tail -4; "
        + "echo focus; dumpsys audio | grep -A4 'Audio Focus stack'; "
        + "echo flux; dumpsys audio | grep -A2 '^- STREAM_MUSIC'; true";

    private readonly Lock _gate = new();

    /// <summary>Phones whose last reading heard the game.</summary>
    private readonly HashSet<string> _heard = new(StringComparer.Ordinal);

    /// <summary>Phones reopened while heard: when, and the silent readings since.</summary>
    private readonly Dictionary<string, (DateTimeOffset At, int Silent)> _waiting = new(StringComparer.Ordinal);

    private readonly HashSet<string> _stuck = new(StringComparer.Ordinal);

    /// <summary>The windows of these phones are reopening.</summary>
    public void Reopened(IEnumerable<string> serials, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(serials);

        lock (_gate)
        {
            // Already waiting counts too: a second reopen comes while the
            // games of the first are still loading, silent.
            foreach (var serial in serials.Where(s => _heard.Contains(s) || _waiting.ContainsKey(s)))
            {
                _waiting[serial] = (now, 0);
            }
        }
    }

    /// <summary>
    /// Takes a reading, and returns true on the one that finds the sound
    /// stuck, once.
    /// </summary>
    public bool Read(string serial, GameSound sound, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sound);

        lock (_gate)
        {
            if (sound.GamePlayers > 0)
            {
                _ = _heard.Add(serial);
                _ = _waiting.Remove(serial);
                _ = _stuck.Remove(serial);
                return false;
            }

            _ = _heard.Remove(serial);

            // No game: nothing left to say. The reopen is kept, a reading may
            // fall between the stop of the games and their relaunch.
            if (sound.GameProcesses == 0)
            {
                _ = _stuck.Remove(serial);
                return false;
            }

            if (!_waiting.TryGetValue(serial, out var waiting))
            {
                return false;
            }

            if (now - waiting.At > Expiry)
            {
                _ = _waiting.Remove(serial);
                return false;
            }

            if (waiting.Silent + 1 < SilentReadings)
            {
                _waiting[serial] = (waiting.At, waiting.Silent + 1);
                return false;
            }

            _ = _waiting.Remove(serial);
            return _stuck.Add(serial);
        }
    }

    /// <summary>
    /// Forgets the phones no longer connected: restarted as the panel said,
    /// or unplugged.
    /// </summary>
    public void ForgetGone(IReadOnlyCollection<string> connected)
    {
        ArgumentNullException.ThrowIfNull(connected);

        lock (_gate)
        {
            _ = _heard.RemoveWhere(s => !connected.Contains(s));
            _ = _stuck.RemoveWhere(s => !connected.Contains(s));

            foreach (var gone in _waiting.Keys.Where(s => !connected.Contains(s)).ToList())
            {
                _ = _waiting.Remove(gone);
            }
        }
    }

    /// <summary>True while this phone's sound is stuck since a reopen.</summary>
    public bool IsStuck(string serial)
    {
        lock (_gate)
        {
            return _stuck.Contains(serial);
        }
    }
}
