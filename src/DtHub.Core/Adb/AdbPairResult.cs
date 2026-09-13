namespace DtHub.Core.Adb;

/// <summary>Outcome of an <c>adb pair</c>.</summary>
public sealed record AdbPairResult(bool Succeeded, string? DeviceGuid = null, string? FailureReason = null)
{
    public static AdbPairResult Success(string? deviceGuid) => new(true, deviceGuid);

    public static AdbPairResult Failure(string? reason) => new(false, null, reason);
}

/// <summary>Outcome of an <c>adb connect</c>.</summary>
public sealed record AdbConnectResult(bool Succeeded, bool AlreadyConnected = false, string? FailureReason = null)
{
    public static readonly AdbConnectResult Connected = new(true);

    public static readonly AdbConnectResult Already = new(true, AlreadyConnected: true);

    public static AdbConnectResult Failure(string? reason) => new(false, false, reason);
}
