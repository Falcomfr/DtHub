namespace DtHub.Core.Papycha;

/// <summary>
/// How often a quest can be done again, as the site's API says it.
/// </summary>
public enum QuestRecurrence
{
    /// <summary>The site does not say, or says something not known here.</summary>
    Unknown,

    /// <summary>Done once and for all.</summary>
    Once,

    /// <summary>Again every day.</summary>
    Daily,

    /// <summary>Again every week.</summary>
    Weekly,

    /// <summary>Again, with no fixed rhythm.</summary>
    Repeatable,

    /// <summary>Again with each season or event.</summary>
    Seasonal,
}
