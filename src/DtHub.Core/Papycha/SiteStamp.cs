namespace DtHub.Core.Papycha;

/// <summary>
/// A way to know the site has changed without rereading it.
///
/// Two numbers are enough: the date of the last modified article and
/// the total number of articles. One changes when a page is
/// corrected, the other when a page appears or disappears.
///
/// The request that returns them weighs ninety seven bytes, against
/// eighteen million to reread the whole site. This is what makes it
/// possible to check often instead of waiting a week.
/// </summary>
/// <param name="Modified">Date of the last modified article.</param>
/// <param name="Posts">Total number of published articles.</param>
public sealed record SiteStamp(DateTimeOffset Modified, int Posts);

/// <summary>
/// A category's fingerprint on the site, kept from one read to the
/// next.
///
/// A single fingerprint for the whole site triggered a reread as
/// soon as a single one of the one thousand twelve articles changed,
/// even one that is never read. They are therefore taken category by
/// category, five requests of ninety seven bytes each, and only the
/// one that changed gets reread.
/// </summary>
/// <param name="Category">The site's category.</param>
/// <param name="Modified">
/// Date of the last article modified in this category.
/// </param>
/// <param name="Posts">Number of articles it holds.</param>
public sealed record CategoryStamp(int Category, DateTimeOffset Modified, int Posts);
