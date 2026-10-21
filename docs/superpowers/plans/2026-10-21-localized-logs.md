# Localize file log rendering

> Execute inline with superpowers:executing-plans. Use
> superpowers:requesting-code-review after integration.

**Goal:** Write DT Hub's own logs in the language currently used by the UI.

**Architecture:** Core resource catalogs map source templates to English,
French and Spanish. Infrastructure renders translated Serilog events through
the existing output template while preserving the original structured event.
The app selects its language before logging startup.

**Tech stack:** .NET 10, resx, Serilog 4.3.0, xUnit, WPF.

- [x] Add formatter/catalog tests and observe missing-feature failures.
- [x] Add the already-used Serilog version as an explicit Infrastructure reference.
- [x] Add LogMessages and its three catalogs, preserving named placeholders.
- [x] Implement LocalizedLogFormatter and route the file sink through it.
- [x] Choose the language before the startup and language-choice entries.
- [x] Check catalog coverage, all placeholders and unmodified raw diagnostics.
- [x] Update conventions/decisions/status, review and run build plus full tests.
- [x] Produce a test executable and inspect localized output without changing
  the player's configured language or closing an active game.

Validation: initial missing formatter/format APIs and four concatenated-template
coverage failures were observed before implementation. Review found partial
trace/span correlation loss; both new regression cases failed before its fix.
All 2,521 tests pass. Release single-file publish succeeds without warnings.
The published app was launched in its existing English setting on French
Windows; startup and subsequent file entries are English, with no warnings or
errors in the observed session. The configurator capture was inspected at
`build/localized-logs-app.png` (ignored). French and Spanish rendering, raw
values and all 233 catalog entries are covered by automated tests.

The local branch and worktree are retained for continued work.
