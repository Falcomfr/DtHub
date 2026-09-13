namespace DtHub.Core.Devices;

/// <summary>
/// How many milliseconds to hold a frame before displaying it.
///
/// scrcpy displays each frame as soon as it arrives. That is the
/// right choice when frames arrive regularly: latency is then at
/// its lowest. But when the connection stutters, the picture
/// freezes for the duration of the stutter, and the game looks
/// like it is lagging even though the throughput is plenty.
///
/// Measured on this machine, two windows open, a static game
/// screen, barely four megabits on a connection that carries
/// eight to thirteen: latency ranged from 4 ms to **223 ms**, for
/// an average of 39 ms. Nothing was saturated, not the PC, whose
/// processor was doing nothing, nor the bandwidth. This was the
/// connection's only visible irregularity.
///
/// A buffer trades this irregularity for a constant delay. The
/// trade is worthwhile as long as the delay stays under the
/// threshold where a click starts to feel sluggish.
/// </summary>
public static class VideoBuffer
{
    /// <summary>
    /// Nothing to compensate for: this is what a wired connection
    /// gives.
    ///
    /// USB has neither neighbors nor interference, and its raw
    /// latency is a gift we do not waste by holding frames for
    /// nothing.
    /// </summary>
    public const int None = 0;

    /// <summary>
    /// Deliberate ceiling.
    ///
    /// The buffer targets **average** jitter, not the peaks. On the
    /// measured connection, the average was 39 ms and the worst
    /// 223: covering the worst would have required a quarter of a
    /// second of delay on every click, which trades one annoyance
    /// for another, worse one. An initial attempt at ninety
    /// milliseconds was in fact criticized for feeling sluggish
    /// before it was even reached.
    /// </summary>
    public const int Ceiling = 60;

    /// <summary>
    /// The buffer suited to a connection, in milliseconds.
    ///
    /// The connection's quality can be read from two things the
    /// device already reports: the received signal strength, and
    /// the share of frames that had to be retransmitted. The band
    /// matters too, 2.4 GHz being shared with the whole
    /// neighborhood, whereas 5 GHz is almost always quiet.
    ///
    /// The tiers are coarse, and that is deliberate: a buffer does
    /// not need to be accurate to the millisecond, it needs to be
    /// of the right order of magnitude.
    /// </summary>
    /// <param name="link">
    /// What the device reports about its connection, or null over USB.
    /// </param>
    public static int MillisecondsFor(WifiLink? link)
    {
        if (link is null)
        {
            return None;
        }

        // A comfortable 5 GHz connection does not stutter enough
        // to deserve a delay: a few frames of lead time are enough
        // to absorb the rest.
        var baseline = link.Is24GHz ? 25 : 10;

        var weak = link.Rssi switch
        {
            >= -55 => 0,
            >= -60 => 5,
            >= -65 => 12,
            >= -70 => 20,
            _ => 30,
        };

        // Retransmissions reveal the neighborhood: they rise when
        // the channel is contested, which signal strength alone
        // does not show.
        var crowded = link.RetryShare switch
        {
            >= 0.20 => 10,
            >= 0.10 => 5,
            _ => 0,
        };

        return Math.Min(Ceiling, baseline + weak + crowded);
    }
}
