using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using DtHub.Core.Devices;
using DtHub.Core.Localization;
using DtHub.Core.Sessions;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Diagnostics;
using Serilog.Events;
using Serilog.Parsing;

namespace DtHub.Tests.Localization;

public sealed class LocalizedLogTests : IDisposable
{
    private const string Startup = "{Product} {Version} démarre.";

    public void Dispose()
    {
        Strings.Speak(null);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Le_message_du_journal_suit_la_langue_de_l_interface(string language)
    {
        Strings.Speak(CultureInfo.GetCultureInfo(language));
        var message = Event(Startup);
        var output = Render(message);

        Assert.Contains("DT Hub 0.9.6", output);
        Assert.Contains("[INF] [session-test] DtHub.Test", output);
        Assert.Equal(language == "fr", output.Contains("démarre", StringComparison.Ordinal));
        Assert.Equal(Startup, message.MessageTemplate.Text);
    }

    [Fact]
    public void Un_message_inconnu_et_la_trace_d_erreur_restent_intacts()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("es"));
        var exception = new InvalidOperationException("adb: device not found");
        var message = Event("Unknown tool output: {Product}", exception);

        var output = Render(message);

        Assert.Contains("Unknown tool output: DT Hub", output);
        Assert.Contains(exception.ToString(), output);
        Assert.Same(exception, message.Exception);
    }

    [Fact]
    public void Le_cache_ne_melange_pas_les_langues_et_ne_modifie_pas_l_evenement()
    {
        var formatter = new LocalizedLogFormatter("{Message:lj}", CultureInfo.InvariantCulture);
        var message = Event(Startup);
        var original = message.MessageTemplate;
        var outputs = new List<string>();
        foreach (var language in new[] { "fr", "en", "es", "fr" })
        {
            Strings.Speak(CultureInfo.GetCultureInfo(language));
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            formatter.Format(message, writer);
            outputs.Add(writer.ToString());
        }

        Assert.Equal(3, outputs.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(outputs[0], outputs[3]);
        Assert.Same(original, message.MessageTemplate);
        Assert.Equal("DT Hub", ((ScalarValue)message.Properties["Product"]).Value);
    }

    [Fact]
    public void Les_identifiants_de_trace_et_les_nombres_sont_conserves()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("en"));
        var trace = ActivityTraceId.CreateRandom();
        var span = ActivitySpanId.CreateRandom();
        var message = new LogEvent(DateTimeOffset.UnixEpoch, LogEventLevel.Warning, null,
            new MessageTemplateParser().Parse("Arrêt rangé en {elapsed} ms."),
            [new LogEventProperty("elapsed", new ScalarValue(12.5))], trace, span);
        var output = Render(message, "{Timestamp:yyyy-MM-dd} [{Level:u3}] {TraceId} {SpanId} {Message:lj}");

        Assert.Contains("1970-01-01 [WRN]", output);
        Assert.Contains(trace.ToString(), output);
        Assert.Contains(span.ToString(), output);
        Assert.Contains("12.5", output);
        Assert.DoesNotContain("Arrêt rangé", output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Un_identifiant_de_correlation_isole_est_conserve(bool traceOnly)
    {
        Strings.Speak(CultureInfo.GetCultureInfo("en"));
        var trace = traceOnly ? ActivityTraceId.CreateRandom() : default;
        var span = traceOnly ? default : ActivitySpanId.CreateRandom();
        var source = Event(Startup);
        var message = new LogEvent(source.Timestamp, source.Level, null, source.MessageTemplate,
            source.Properties.Select(pair => new LogEventProperty(pair.Key, pair.Value)), trace, span);
        var output = Render(message, "{TraceId} {SpanId} {Message:lj}");

        Assert.Contains(traceOnly ? trace.ToString() : span.ToString(), output);
        Assert.Contains("DT Hub", output);
    }

    [Fact]
    public void Les_catalogues_portent_les_memes_messages_et_parametres()
    {
        var resources = new ResourceManager("DtHub.Core.Localization.LogMessages", typeof(Strings).Assembly);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in AppLanguage.Supported)
        {
            var culture = language == AppLanguage.Neutral ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language);
            var entries = resources.GetResourceSet(culture, true, false)!;
            var current = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry entry in entries)
            {
                var key = (string)entry.Key;
                var translated = (string)entry.Value!;
                Assert.False(string.IsNullOrWhiteSpace(translated));
                Assert.Equal(Properties(key), Properties(translated));
                current.Add(key);
            }

            Assert.True(current.Count >= 185);
            if (keys.Count == 0) keys = current;
            else Assert.Equal(keys.Order(), current.Order());
        }
    }

    [Fact]
    public void Chaque_modele_de_journal_a_sa_traduction_y_compris_les_chaines_concatenees()
    {
        const string literal = """
            (?<literal>"(?:[^"\\]|\\.)*")
            """;
        var calls = new Regex(
            """
            (?:Message\s*=\s*|(?:\bLog|Serilog\.Log|_logger|logger)\.(?:Information|Warning|Error|Debug|Fatal|Verbose|LogInformation|LogWarning|LogError|LogDebug)\(\s*(?:[^"()]*?,\s*)?|LogMessages\.(?:Translate|Format)\(\s*)
            """ + literal + "(?:\\s*\\+\\s*" + literal + ")*");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DtHub.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var resources = new ResourceManager("DtHub.Core.Localization.LogMessages", typeof(Strings).Assembly);
        var entries = resources.GetResourceSet(CultureInfo.GetCultureInfo("fr"), true, false)!;
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory.FullName, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match match in calls.Matches(File.ReadAllText(file)))
            {
                var template = string.Concat(match.Groups["literal"].Captures
                    .Select(capture => JsonSerializer.Deserialize<string>(capture.Value)));
                if (entries.GetString(template) is null)
                {
                    missing.Add($"{Path.GetFileName(file)}: {template}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("en", "unknown", "debugging", "no driver", " at ", "bounds", "layer")]
    [InlineData("es", "desconocida", "depuración", "sin controlador", " en ", "límites", "capa")]
    [InlineData("fr", "inconnu", "débogage", "aucun pilote", " en ", "cadre", "couche")]
    public void Les_details_possedes_sont_traduits_sans_modifier_les_donnees(
        string language, string unknown, string debugging, string driver, string position, string bounds, string layer)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Strings.Speak(culture);
        Assert.Contains(unknown, LogMessages.Format("Ctrl+E, écran lu {0}, {1}",
            LogMessages.Translate("inconnu"), "raw-state"));
        Assert.Contains("raw-state", LogMessages.Format("Ctrl+E, écran lu {0}, {1}",
            LogMessages.Translate("inconnu"), "raw-state"));
        var usb = UsbPresence.SummarizeForLog(
            [new UsbPresence(@"USB\VID_1234&PID_5678\private-serial", ["USB\\Class_FF&SubClass_42&Prot_01"], null)]);
        Assert.Contains(debugging, usb);
        Assert.Contains(driver, usb);
        Assert.Contains("VID_1234&PID_5678", usb);
        Assert.DoesNotContain("private-serial", usb);
        var rect = new ScreenRect(-123, 456, 1920, 1080);
        Assert.Equal($"1920x1080{position}(-123, 456)", rect.ToLogString());
        Assert.Equal("1920x1080 en (-123, 456)", rect.ToString());
        var state = AndroidAppLauncher.GameWindowState("", "", "", 1, "test.package", culture);
        Assert.Contains(bounds, state);
        Assert.Contains(layer, state);
    }

    private static string[] Properties(string template) =>
        [.. new MessageTemplateParser().Parse(template).Tokens.OfType<PropertyToken>()
            .Select(token => token.ToString()).Order(StringComparer.Ordinal)];

    private static LogEvent Event(string template, Exception? exception = null) =>
        new(DateTimeOffset.UnixEpoch, LogEventLevel.Information, exception,
            new MessageTemplateParser().Parse(template),
            [new("Product", new ScalarValue("DT Hub")), new("Version", new ScalarValue("0.9.6")),
             new("Session", new ScalarValue("session-test")), new("SourceContext", new ScalarValue("DtHub.Test"))]);

    private static string Render(LogEvent message,
        string template = "[{Level:u3}] [{Session}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        new LocalizedLogFormatter(template, CultureInfo.InvariantCulture).Format(message, output);
        return output.ToString();
    }
}
