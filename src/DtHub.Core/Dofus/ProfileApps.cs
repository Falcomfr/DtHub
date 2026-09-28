using DtHub.Core.Android;
using DtHub.Core.Users;

namespace DtHub.Core.Dofus;

/// <summary>
/// The applications one Android profile can open, the game left out.
/// </summary>
/// <param name="UserId">The profile.</param>
/// <param name="UserName">Its name, as the phone reports it.</param>
/// <param name="Type">Main, clone, work profile.</param>
/// <param name="Apps">Its applications, the phone's own last.</param>
public sealed record ProfileApps(
    int UserId,
    string UserName,
    AndroidUserType Type,
    IReadOnlyList<DeviceApp> Apps);
