using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class SettingsBackupTests
{
    private static string Fichier(int version) => $$"""
        {
          "schemaVersion": {{version}},
          "instances": [],
          "quality": "Medium"
        }
        """;

    [Fact]
    public void Un_fichier_de_cette_version_est_utilisable()
    {
        var found = SettingsBackup.Inspect(Fichier(AppSettingsDocument.CurrentSchemaVersion));

        Assert.Equal(BackupVerdict.Usable, found.Verdict);
        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, found.SchemaVersion);
    }

    [Fact]
    public void Un_fichier_plus_ancien_est_utilisable()
    {
        // The ordinary path knows how to migrate forward, that is its job.
        Assert.Equal(BackupVerdict.Usable, SettingsBackup.Inspect(Fichier(3)).Verdict);
    }

    [Fact]
    public void Un_fichier_plus_recent_est_refuse()
    {
        // The ordinary path would bring it back down to the current
        // version, losing whatever it carried beyond that. Acceptable
        // for a local file one comes back to, not for an import where
        // someone believes they are restoring something.
        var found = SettingsBackup.Inspect(Fichier(AppSettingsDocument.CurrentSchemaVersion + 1));

        Assert.Equal(BackupVerdict.TooNew, found.Verdict);
        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion + 1, found.SchemaVersion);
    }

    [Theory]
    [InlineData("""{ "quelque": "chose" }""")]
    [InlineData("""{ "schemaVersion": 9 }""")]
    [InlineData("""{ "instances": [] }""")]
    [InlineData("""{ "schemaVersion": "neuf", "instances": [] }""")]
    [InlineData("""{ "schemaVersion": 9, "instances": {} }""")]
    public void Un_json_qui_n_est_pas_le_notre_est_refuse(string json)
    {
        Assert.Equal(BackupVerdict.Foreign, SettingsBackup.Inspect(json).Verdict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pas du json")]
    [InlineData("[]")]
    [InlineData("\"une chaîne\"")]
    [InlineData("""{ "schemaVersion": 9, "instances": [ """)]
    public void Un_contenu_illisible_est_refuse(string? json)
    {
        Assert.Equal(BackupVerdict.Unreadable, SettingsBackup.Inspect(json).Verdict);
    }
}
