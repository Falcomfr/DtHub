namespace DtHub.Core.Windows;

/// <summary>
/// A window the window manager can place: a scrcpy session, or an
/// iPhone mirror window.
/// </summary>
public interface IGameWindow
{
    /// <summary>Session id; iOS: "ios:" + udid.</summary>
    string Id { get; }

    /// <summary>Account key.</summary>
    string Key { get; }

    nint WindowHandle { get; }

    bool IsAlive { get; }

    /// <summary>0 when unknown.</summary>
    double SourceAspectRatio { get; }

    string WindowTitle { get; }

    /// <summary>Owner of the window; iOS: DT Hub's own pid.</summary>
    int ProcessId { get; }
}
