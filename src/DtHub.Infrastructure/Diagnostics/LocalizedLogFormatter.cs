using System.Collections.Concurrent;

using DtHub.Core.Localization;

using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace DtHub.Infrastructure.Diagnostics;

/// <summary>
/// Localizes the file's message while keeping the structured source event,
/// metadata, exception and raw diagnostic values intact for other sinks.
/// </summary>
public sealed class LocalizedLogFormatter(string outputTemplate, IFormatProvider? formatProvider = null) : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _output = new(outputTemplate, formatProvider);
    private readonly ConcurrentDictionary<(string Language, string Template), MessageTemplate> _templates = new();

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var culture = Strings.Spoken;
        var original = logEvent.MessageTemplate.Text;
        var translated = LogMessages.Translate(original, culture);

        if (string.Equals(original, translated, StringComparison.Ordinal))
        {
            _output.Format(logEvent, output);
            return;
        }

        var template = _templates.GetOrAdd((culture.Name, original),
            _ => new MessageTemplateParser().Parse(translated));
        var properties = logEvent.Properties.Select(pair => new LogEventProperty(pair.Key, pair.Value));
        var localized = new LogEvent(logEvent.Timestamp, logEvent.Level, logEvent.Exception,
            template, properties, logEvent.TraceId ?? default, logEvent.SpanId ?? default);

        _output.Format(localized, output);
    }
}
