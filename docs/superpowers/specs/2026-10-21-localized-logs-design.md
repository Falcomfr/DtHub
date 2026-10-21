# Logs follow the application language

DT Hub's own log messages follow the interface language (English, French or
Spanish), chosen from the application setting and Windows with the existing
AppLanguage rule. The language stays the one in force until restart, like the
interface. Choose it before the first startup message.

Keep constant structured source templates for generated LoggerMessage methods.
Translate only their rendering in the file sink through a dedicated resource
catalog and formatter. The three catalogs use the original template as key,
preserving parameter names and format hints. Unknown/external templates pass
through unchanged. Preserve timestamps, levels, session/source fields,
properties, trace/span identifiers, exceptions and raw tool output. Cache parsed
translations by template and language, without mutating the original event.

Use Serilog 4.3.0, already in the application dependency graph, explicitly in
Infrastructure. Core owns the resource catalog and has no Serilog dependency.
Tests cover all languages, parameter parity, catalog coverage, formatting and
exception preservation, culture changes and unknown messages. The diagnostic
digest relies on levels and session markers, which remain unchanged.

Update the repository's earlier French-only logging convention to this request.
The developer debug sink may keep source templates; the user-facing .log files
and reports drawn from them follow the application language.
