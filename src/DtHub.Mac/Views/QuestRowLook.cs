using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.Mac.Views;

/// <summary>
/// The look of a row of the guides' list and of a prerequisite, the
/// PC's DataTemplate.Triggers (src/DtHub.App/Windows/QuestWindow.xaml) in
/// one place: Avalonia has no data triggers, and a row is an immutable
/// record, so what it shows is worked out once from it. The parameter
/// names what is asked; the drawings and the order in which the PC's
/// triggers override each other are the PC's.
/// </summary>
internal sealed class QuestRowLook : IValueConverter
{
    private const double SectionSize = 14;

    private static readonly Dictionary<string, Geometry> Shapes = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            QuestNode node => Row(node, parameter as string ?? string.Empty),
            QuestNeed need => Need(need, parameter as string ?? string.Empty),
            _ => Avalonia.AvaloniaProperty.UnsetValue,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static object? Row(QuestNode node, string part) => part switch
    {
        // What the row is: a subheading is set apart from what it heads.
        "Filet" => node.Kind is QuestNodeKind.Header or QuestNodeKind.Section,
        "Trait" => node.InSuccess,
        "LineMargin" => node.Spaced
            ? new Thickness(0, 3, 0, 14)
            : node.InSuccess
                ? new Thickness(0)
                : node.Kind switch
                {
                    QuestNodeKind.Header or QuestNodeKind.Success => new Thickness(0, 16, 0, 4),
                    QuestNodeKind.Section => new Thickness(0, 18, 0, 5),
                    _ => new Thickness(0, 3),
                },
        "LineOpacity" => node.Status is QuestStatus.Blocked or QuestStatus.Ignored ? 0.5 : 1.0,
        "LabelWeight" => node.Kind switch
        {
            QuestNodeKind.Header or QuestNodeKind.Success => FontWeight.SemiBold,
            QuestNodeKind.Section => FontWeight.Bold,
            _ => FontWeight.Normal,
        },
        "LabelBrush" => Brush(node.Kind switch
        {
            QuestNodeKind.Header or QuestNodeKind.Success => "AccentBrush",
            QuestNodeKind.Pending => "TextMutedBrush",
            _ => "TextBrush",
        }),
        "LabelMargin" => node.Nested
            ? new Thickness(32, 0, 0, 0)
            : node.InSuccess ? new Thickness(9, 3, 0, 3) : new Thickness(0),

        // A results group's title a point above the rows, as on the PC
        // (13 over 12 there; the Mac's rows are 13); the others inherit.
        "LabelSize" => node.Kind == QuestNodeKind.Section ? SectionSize : Avalonia.AvaloniaProperty.UnsetValue,

        // The icon, set by the row's nature rather than its kind.
        "NatureVisible" => node.Glyph != QuestNodeGlyph.None,
        "NatureData" => Shape(NatureShape(node.Glyph)),
        "NatureStroke" => node.Glyph switch
        {
            QuestNodeGlyph.Success => Brush("GlyphSuccessBrush"),
            QuestNodeGlyph.Place or QuestNodeGlyph.Route => Brush("GlyphPlaceBrush"),
            QuestNodeGlyph.Family => Brush("GlyphFamilyBrush"),
            QuestNodeGlyph.None => Brush("TextMutedBrush"),
            _ => null,
        },
        "NatureFill" => node.Glyph is QuestNodeGlyph.Quests or QuestNodeGlyph.Dungeons
            or QuestNodeGlyph.Raids or QuestNodeGlyph.Lairs
            ? Brush("GlyphDungeonBrush")
            : null,
        "NatureCap" => node.Glyph == QuestNodeGlyph.Route ? PenLineCap.Flat : PenLineCap.Round,
        "NatureColumn" => node.Nested ? 1 : 0,
        "NatureAlign" => node.Nested ? HorizontalAlignment.Left : HorizontalAlignment.Center,
        "NatureMargin" => node.Nested ? new Thickness(7, 0, 0, 0) : new Thickness(0),
        "HookUp" => node.Nested,
        "HookDown" => node.AfterPath,

        // The right-hand text: a fixed, right-aligned column on a zone with
        // a tally, kept even when empty.
        "DetailVisible" => node.Tally is not null || !string.IsNullOrWhiteSpace(node.Detail),
        "DetailMargin" => string.IsNullOrWhiteSpace(node.Duration) ? new Thickness(8, 0, 0, 0) : new Thickness(8, 0, 4, 0),
        "DetailWidth" => node.Tally is not null ? 52.0 : double.NaN,
        "DetailAlign" => node.Tally is not null ? TextAlignment.Right : TextAlignment.Left,

        // The gains: hidden (their place kept) on a quest row without data,
        // gone on rows that are neither quest nor dungeon.
        "QuestOrDungeon" => node.Quest is not null || node.Dungeon is not null,
        "GainOpacity" => node.GainXp is null ? 0.0 : 1.0,
        "XpValueVisible" => node.GainXp is not "",
        "KamasValueVisible" => node.GainKamas is not null,
        "FinishOpacity" => node.Finishes == 0 ? 0.0 : 1.0,
        "TimeOpacity" => string.IsNullOrWhiteSpace(node.Duration) ? 0.0 : 1.0,

        // The priority marks: a flame, the same flame hollow, a dot.
        "TierVisible" => node.Tier is >= 1 and <= 3 && (node.Quest is not null || node.Dungeon is not null),
        "TierData" => Shape(node.Tier switch
        {
            3 => "M 6,0.8 C 6.9,3 9.8,4.6 9.8,7.6 A 3.8,3.6 0 0 1 2.2,7.6 C 2.2,5.9 3.2,4.9 4,3.8 C 4.3,5 4.9,5.6 5.5,5.8 C 5.2,4.1 5.3,2.4 6,0.8 Z",
            2 => "M 6,1.4 C 6.8,3.4 9.4,4.8 9.4,7.6 A 3.4,3.2 0 0 1 2.6,7.6 C 2.6,6 3.5,5.1 4.1,4.3 C 4.4,5.2 4.9,5.7 5.5,5.9 C 5.3,4.4 5.4,2.9 6,1.4 Z",
            1 => "M 4,7 A 2,2 0 1 0 8,7 A 2,2 0 1 0 4,7 Z",
            _ => null,
        }),
        "TierFill" => node.Tier == 2 ? null : Brush("GlyphPriorityBrush"),
        "TierThickness" => node.Tier == 2 ? 1.3 : 0.0,

        // The tracker's mark: a check, an hourglass, a barred circle, a dash.
        "MarkVisible" => node.Quest is not null
            && node.Status is QuestStatus.Completed or QuestStatus.InProgress or QuestStatus.Blocked or QuestStatus.Ignored,
        "MarkData" => Shape(node.Status switch
        {
            QuestStatus.Completed => "M 2,6.5 L 5,9.5 L 10.5,3",
            QuestStatus.InProgress => "M 2.5,1.5 H 9.5 M 2.5,10.5 H 9.5 M 3.5,1.5 L 6,6 L 3.5,10.5 M 8.5,1.5 L 6,6 L 8.5,10.5",
            QuestStatus.Blocked => "M 1.5,6 A 4.5,4.5 0 1 0 10.5,6 A 4.5,4.5 0 1 0 1.5,6 M 2.8,2.8 L 9.2,9.2",
            QuestStatus.Ignored => "M 3,6 H 9",
            _ => null,
        }),
        "MarkStroke" => Brush(node.Status is QuestStatus.Blocked or QuestStatus.Ignored ? "TextMutedBrush" : "AccentBrush"),
        "MarkTip" => node.Status switch
        {
            QuestStatus.Completed => Strings.Get("QuestStatusCompleted"),
            QuestStatus.InProgress => Strings.Get("QuestStatusInProgress"),
            QuestStatus.Blocked => Strings.Get("QuestStatusBlocked"),
            QuestStatus.Ignored => Strings.Get("QuestStatusIgnored"),
            _ => null,
        },

        // The rhythms: a calendar page, two arrows chasing round, a leaf.
        "RhythmVisible" => node.Quest is not null && RhythmShape(node.Recurrence) is not null,
        "RhythmData" => Shape(RhythmShape(node.Recurrence)),

        // The lock, only while the prerequisites are not met; in the warning
        // colour when the tracker knows they are not.
        "LockVisible" => node.Quest is not null && node.HasNeeds && node.NeedsMet != true,
        "LockStroke" => Brush(node.NeedsMet == false ? "WarningBrush" : "GlyphNeedBrush"),

        "CheckVisible" => node.Checked is not null,
        "HasFacts" => node.Facts is not null,

        // A zone's progress.
        "StartedOpacity" => node.Tally?.Started == 0 ? 0.0 : 1.0,
        "CountCaseVisible" => string.IsNullOrWhiteSpace(node.Tally?.PassingTip),
        "CountOpacity" => node.Tally?.Total == 0 ? 0.0 : 1.0,
        "CountVisible" => node.Tally?.IsComplete != true,
        "CompleteVisible" => node.Tally?.IsComplete == true,
        _ => Avalonia.AvaloniaProperty.UnsetValue,
    };

    /// <summary>A prerequisite: the dot for items and free text, a check done, a cross missing.</summary>
    private static object? Need(QuestNeed need, string part) => part switch
    {
        "Dot" => need.Met is null,
        "MarkVisible" => need.Met is not null,
        "MarkData" => Shape(need.Met == false ? "M 1.5,1.5 L 7.5,7.5 M 7.5,1.5 L 1.5,7.5" : "M 0.7,4.6 L 3.2,7 L 8,1.8"),
        "MarkStroke" => Brush(need.Met == false ? "WarningBrush" : "SuccessBrush"),
        _ => Avalonia.AvaloniaProperty.UnsetValue,
    };

    private static string? NatureShape(QuestNodeGlyph glyph) => glyph switch
    {
        // A star: what you obtain.
        QuestNodeGlyph.Success =>
            "M 7.5,2 L 9.2,5.9 L 13.3,6.4 L 10.3,9.2 L 11.1,13.2 L 7.5,11.2 L 3.9,13.2 L 4.7,9.2 L 1.7,6.4 L 5.8,5.9 Z",

        // A pin: a place you're going to.
        QuestNodeGlyph.Place =>
            "M 7.5,13.6 C 7.5,13.6 12.3,8.6 12.3,6.1 A 4.8,4.8 0 1 0 2.7,6.1 C 2.7,8.6 7.5,13.6 7.5,13.6 Z "
            + "M 7.5,6.1 m -1.7,0 a 1.7,1.7 0 1 0 3.4,0 a 1.7,1.7 0 1 0 -3.4,0",

        // A road in perspective: two shoulders, a dashed centre line.
        QuestNodeGlyph.Route =>
            "M 3.2,13.6 L 5.5,2.4 M 11.8,13.6 L 9.5,2.4 M 7.5,12.4 V 10.2 M 7.5,8.4 V 6.4 M 7.5,4.6 V 3.2",

        // A bookmark: a family of quests.
        QuestNodeGlyph.Family => "M 4,2.4 L 11,2.4 L 11,12.9 L 7.5,10.1 L 4,12.9 Z",

        // A scroll.
        QuestNodeGlyph.Quests =>
            "M 3.7,2.7 H 11.3 A 1.05,1.05 0 0 1 11.3,4.8 H 3.7 A 1.05,1.05 0 0 1 3.7,2.7 Z "
            + "M 4.1,4.8 H 10.9 V 10.2 H 4.1 Z "
            + "M 3.7,10.2 H 11.3 A 1.05,1.05 0 0 1 11.3,12.3 H 3.7 A 1.05,1.05 0 0 1 3.7,10.2 Z "
            + "M 5.6,6.2 H 9.4 V 7.0 H 5.6 Z M 5.6,8.0 H 9.4 V 8.8 H 5.6 Z",

        // A crenellated tower.
        QuestNodeGlyph.Dungeons =>
            "M 3.5,2.3 H 5.7 V 4.6 H 6.4 V 2.3 H 8.6 V 4.6 H 9.3 V 2.3 H 11.5 V 13.1 H 3.5 Z "
            + "M 6.3,13.1 V 9.9 A 1.2,1.2 0 0 1 8.7,9.9 V 13.1 Z M 6.9,5.9 H 8.1 V 7.9 H 6.9 Z",

        // A skull: the raid is the fight that kills.
        QuestNodeGlyph.Raids =>
            "M 3.5,8 A 4,4.4 0 0 1 11.5,8 L 11.5,9.8 L 3.5,9.8 Z M 5.4,9.8 H 9.6 L 8.9,12.6 H 6.1 Z "
            + "M 6,7.1 m -1.3,0 a 1.3,1.3 0 1 0 2.6,0 a 1.3,1.3 0 1 0 -2.6,0 "
            + "M 9,7.1 m -1.3,0 a 1.3,1.3 0 1 0 2.6,0 a 1.3,1.3 0 1 0 -2.6,0 "
            + "M 6.75,9.8 H 7.2 V 12.35 H 6.75 Z M 7.8,9.8 H 8.25 V 12.3 H 7.8 Z",

        // A paw print: a beast's den.
        QuestNodeGlyph.Lairs =>
            "M 4.35,9.9 a 3.15,2.6 0 1 0 6.3,0 a 3.15,2.6 0 1 0 -6.3,0 Z "
            + "M 4.25,4.8 a 1.3,1.75 0 1 0 2.6,0 a 1.3,1.75 0 1 0 -2.6,0 Z "
            + "M 8.15,4.8 a 1.3,1.75 0 1 0 2.6,0 a 1.3,1.75 0 1 0 -2.6,0 Z "
            + "M 1.6,7 a 1.15,1.55 0 1 0 2.3,0 a 1.15,1.55 0 1 0 -2.3,0 Z "
            + "M 11.1,7 a 1.15,1.55 0 1 0 2.3,0 a 1.15,1.55 0 1 0 -2.3,0 Z",
        _ => null,
    };

    private static string? RhythmShape(QuestRecurrence recurrence) => recurrence switch
    {
        QuestRecurrence.Daily => "M 2.5,2.5 H 12.5 V 13.5 H 2.5 Z M 2.5,4.5 H 12.5 M 6.6,7.8 L 7.8,7 V 11",
        QuestRecurrence.Weekly => "M 2.5,2.5 H 12.5 V 13.5 H 2.5 Z M 2.5,4.5 H 12.5 M 5.9,7 H 9.1 L 7.1,11",
        QuestRecurrence.Repeatable =>
            "M 3.27,6 A 4.5,4.5 0 0 1 11.73,6 M 12.52,3.94 L 11.73,6 L 9.84,4.87 "
            + "M 11.73,9 A 4.5,4.5 0 0 1 3.27,9 M 2.48,11.06 L 3.27,9 L 5.16,10.13",
        QuestRecurrence.Seasonal => "M 3,12.5 C 3,6.5 6,3 12.5,2.5 C 12,9 8.5,12.5 3,12.5 Z M 3,12.5 L 8.5,7",
        _ => null,
    };

    private static Geometry? Shape(string? data)
    {
        if (data is null)
        {
            return null;
        }

        if (!Shapes.TryGetValue(data, out var shape))
        {
            shape = Geometry.Parse(data);
            Shapes[data] = shape;
        }

        return shape;
    }

    /// <summary>A brush of the palette (Themes/PaletteDark.axaml).</summary>
    private static IBrush? Brush(string key) =>
        Application.Current is { } application
        && application.TryGetResource(key, application.ActualThemeVariant, out var found)
            ? found as IBrush
            : null;
}

/// <summary>A column width of the list: true when some row of it fills the column.</summary>
internal sealed class NonZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width && width > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
