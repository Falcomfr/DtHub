namespace DtHub.Core.Papycha;

/// <summary>
/// A way to know the site has changed without reading it again: the date
/// its public API announces for its last update.
///
/// The request that returns it weighs a few hundred bytes, against the
/// whole catalog to read again. This is what makes it possible to check
/// often instead of waiting a week.
/// </summary>
/// <param name="Modified">Date of the API's last update.</param>
public sealed record SiteStamp(DateTimeOffset Modified);
