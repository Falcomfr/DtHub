namespace DtHub.Core.IPhone;

public enum IosWindowState
{
    /// <summary>The stream is starting, first time or again.</summary>
    Opening,

    Open,

    /// <summary>The device left: the window waits for it in place.</summary>
    Unplugged,

    /// <summary>The stream stopped on an error: the window waits to be opened again.</summary>
    Failed,

    Closed,
}

/// <summary>Why an iPhone's window ended.</summary>
public enum IosEnd
{
    /// <summary>The player's cross on the window.</summary>
    ByHand,

    /// <summary>DT Hub closed it: a button, a team, quitting.</summary>
    ByUs,

    /// <summary>Out of attempts after failures, as Android's recovery.</summary>
    GaveUp,
}

/// <summary>
/// The life of one iOS account's window, as an Android session's: an
/// unplug pauses it, a failure opens it again a few times, a close ends it.
/// </summary>
public sealed class IosWindowLife
{
    public IosWindowState State { get; private set; } = IosWindowState.Opening;

    /// <summary>Openings made again after a failure, as Android counts them.</summary>
    public int Attempts { get; private set; }

    /// <summary>Playtime runs only while the game is shown.</summary>
    public bool CountsPlaytime => State == IosWindowState.Open;

    /// <summary>The stream serves.</summary>
    public void Opened() => State = IosWindowState.Open;

    public void Unplug()
    {
        if (State != IosWindowState.Closed)
        {
            State = IosWindowState.Unplugged;
        }
    }

    /// <summary>The device is back, or a failed stream starts again.</summary>
    public void Replugged()
    {
        if (State is IosWindowState.Unplugged or IosWindowState.Failed)
        {
            State = IosWindowState.Opening;
        }
    }

    /// <summary>
    /// The stream stopped on an error: <c>null</c> while an attempt is
    /// left (<see cref="State"/> is then <see cref="IosWindowState.Failed"/>),
    /// <see cref="IosEnd.GaveUp"/> once <paramref name="maxAttempts"/> were made.
    /// </summary>
    public IosEnd? Fail(int maxAttempts)
    {
        if (Attempts >= maxAttempts)
        {
            State = IosWindowState.Closed;
            return IosEnd.GaveUp;
        }

        Attempts++;
        State = IosWindowState.Failed;
        return null;
    }

    /// <summary>A quiet spell since the last failure: the attempts start over, as Android's.</summary>
    public void Forget() => Attempts = 0;

    public IosEnd Close(bool byHand)
    {
        State = IosWindowState.Closed;
        return byHand ? IosEnd.ByHand : IosEnd.ByUs;
    }

    /// <summary>Closed by the player's cross, the account leaves the next startup (D207).</summary>
    public static bool KeepsStartup(IosEnd end) => end != IosEnd.ByHand;
}
