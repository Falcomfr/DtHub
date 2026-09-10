using System.Text.Json;

namespace DtHub.Core.Settings;

/// <summary>Ce qu'on peut dire d'un fichier de réglages qu'on nous tend.</summary>
public enum BackupVerdict
{
    /// <summary>Lisible et de cette version, ou d'une plus ancienne.</summary>
    Usable,

    /// <summary>Ce n'est pas du JSON, ou pas un objet.</summary>
    Unreadable,

    /// <summary>C'est du JSON, mais ce ne sont pas nos réglages.</summary>
    Foreign,

    /// <summary>Écrit par une version plus récente que celle-ci.</summary>
    TooNew,
}

/// <summary>Ce que l'inspection a trouvé.</summary>
public readonly record struct BackupInspection(BackupVerdict Verdict, int SchemaVersion);

/// <summary>
/// Emporter ses réglages d'un poste à l'autre, et les retrouver.
///
/// Sert au déménagement, et de filet quand le fichier de réglages s'abîme.
///
/// **Le piège est à l'import, et il est précis.** Le chargement ordinaire est
/// volontairement tolérant : un fichier venu d'une version plus récente est
/// ramené à la version courante, et ce qu'il portait en plus est perdu. C'est
/// acceptable pour un fichier local qu'on retrouve, où l'alternative serait de
/// ne plus démarrer. **Ce ne l'est pas pour un import**, où quelqu'un croit
/// restaurer et se retrouverait avec des réglages amputés en silence.
///
/// L'inspection lit donc la version <b>avant</b> de laisser le chemin ordinaire
/// faire son travail, et refuse franchement ce qu'elle ne sait pas lire.
/// </summary>
public static class SettingsBackup
{
    /// <summary>Le nom proposé au moment d'enregistrer.</summary>
    public const string SuggestedFileName = "dthub-reglages.json";

    /// <summary>
    /// Ce qu'on peut dire du contenu qu'on nous tend, sans rien en appliquer.
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
            // Un fichier tronqué, une pièce jointe qui n'en était pas une :
            // on le dit, on n'essaie pas de deviner.
            return new BackupInspection(BackupVerdict.Unreadable, 0);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new BackupInspection(BackupVerdict.Unreadable, 0);
        }

        // Deux marques suffisent à reconnaître les nôtres, et aucune n'est
        // devinable : la version de schéma, et la liste des comptes. Un JSON
        // quelconque n'a ni l'une ni l'autre.
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
