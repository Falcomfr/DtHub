namespace DtHub.Core.Dofus;

/// <summary>
/// What an attempt to add an account produced, ready to be
/// displayed.
/// </summary>
/// <param name="Succeeded">
/// True when the profile is created and the game is ready inside it.
/// </param>
/// <param name="Message">
/// What is told to the user about it, whether success or failure.
/// </param>
/// <param name="UserId">The created profile, or -1 if there is none.</param>
public sealed record AccountAddition(bool Succeeded, string Message, int UserId = -1);
