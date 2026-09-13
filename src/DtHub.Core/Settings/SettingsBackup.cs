using System.Text.Json;

namespace DtHub.Core.Settings;

/// <summary>What can be said about a settings file we are handed.</summary>
public enum BackupVerdict
{
    /// <summary>Readable and of this version, or an older one.</summary>
    Usable,

    /// <summary>This is not JSON, or not an object.</summary>
    Unreadable,

    /// <summary>This is JSON, but not our settings.</summary>
    Foreign,

    /// <summary>Written by a version more recent than this one.</summary>
    TooNew,
}

/// <summary>What the inspection found.</summary>
public readonly record struct BackupInspection(BackupVerdict Verdict, int SchemaVersion);

/// <summary>
/// Carrying your settings from one machine to another, and finding
/// them again.
///
/// Used for moving machines, and as a safety net when the settings
/// file gets damaged.
///
/// **The trap is on import, and it is a precise one.** Ordinary
/// loading is deliberately tolerant: a file coming from a more
/// recent version is brought back down to the current version, and
/// whatever it carried beyond that is lost. That is acceptable for a
/// local file being found again, where the alternative would be to
/// no longer start at all. **It is not acceptable for an import**,
/// where someone believes they are restoring and would end up with
/// settings silently amputated.
///
/// The inspection therefore reads the version <b>before</b> letting
/// the ordinary path do its work, and plainly refuses what it cannot
/// read.
/// </summary>
public static class SettingsBackup
{
    /// <summary>The name suggested when saving.</summary>
    public const string SuggestedFileName = "dthub-reglages.json";

    /// <summary>
    /// What can be said about the content we are handed, without
    /// applying any of it.
    /// </summary>
    public static BackupInspection Inspect(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BackupInspection(BackupVerdict.Unreadable, 0);
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(json);

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // A truncated file, an attachment that was not really
            // one: we say so, we do not try to guess.
            return new BackupInspection(BackupVerdict.Unreadable, 0);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new BackupInspection(BackupVerdict.Unreadable, 0);
        }

        // Two markers are enough to recognize our own, and neither
        // can be guessed: the schema version, and the list of
        // accounts. Any random JSON has neither.
        if (!root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !root.TryGetProperty("instances", out var instances)
            || instances.ValueKind != JsonValueKind.Array)
        {
            return new BackupInspection(BackupVerdict.Foreign, 0);
        }

        var found = version.GetInt32();

        return found > AppSettingsDocument.CurrentSchemaVersion
            ? new BackupInspection(BackupVerdict.TooNew, found)
            : new BackupInspection(BackupVerdict.Usable, found);
    }
}
