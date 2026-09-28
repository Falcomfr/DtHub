namespace DtHub.Core.Dofus;

/// <summary>
/// An application the user asked to see in the list, on one profile of
/// one phone. It is already installed there: showing it installs and
/// copies nothing.
/// </summary>
/// <param name="DeviceId">Stable identity of the phone.</param>
/// <param name="UserId">Android profile it lives on.</param>
/// <param name="PackageName">Its package.</param>
/// <param name="Label">The name the phone gives it.</param>
public sealed record ShownApp(string DeviceId, int UserId, string PackageName, string Label)
{
    /// <summary>Same shape as <see cref="DofusInstance.Key" />.</summary>
    public string Key => $"{DeviceId}|{UserId}|{PackageName}";
}
