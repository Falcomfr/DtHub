using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// A section's title as the window displays it.
///
/// The catalogue keeps the sections under their French names, because
/// <see cref="QuestZoneOrder"/> ranks them by those names: the translation
/// happens here, at display and in search, and nowhere else. A section the
/// resources do not know (the site adds one) keeps its French title.
/// </summary>
public static class QuestSectionTitle
{
    public static string Of(QuestSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var translated = Strings.Spoken.TwoLetterISOLanguageName == "fr"
            ? null
            : Strings.Optional("QuestSection" + section.Id);

        return translated ?? QuestZoneOrder.DisplayName(section.Name);
    }
}
