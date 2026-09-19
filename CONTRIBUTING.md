# Contributing

Thanks for the interest. This repository has unusual conventions, firmly
held: read `AGENTS.md` before writing a line. It says what is forbidden, and
why.

## What will never get in

**Any game automation.** Bot, macro, action replay, screen recognition for
playing, input mirrored across accounts, any way around a limit the game
sets. One user input is one action, on one account, and on one only. This is
not a matter of priority, it is the boundary of the project, and a proposal
in that direction will be closed without discussion.

Also excluded: low level keyboard hooks, any request for elevation, any
handling of the antivirus, and PowerShell, Node or Python at application
runtime.

## Before opening a pull request

```
dotnet build DtHub.slnx -warnaserror     # zero warnings, that is the target
dotnet test DtHub.slnx                    # all green
```

The pipeline replays both. A request that breaks them will not be reviewed.

**Write a test.** It must name a behaviour, not a method:
`Un_mdns_muet_laisse_l_appairage_acquis_et_demande_le_port`, not
`TestPairing2`. The tests in this repository read as a specification, and
that is what makes them useful.

**Writing conventions.** Identifiers in English. A comment says *why*, never
*what*: the code already says what it does. Never an em dash.

**The language.** The repository is written in English: documents, code
comments, commit messages, release notes, issue templates. What cannot exist
in two versions is written once, in English.

Five things stay French on purpose, and it is better to name them than to
let them read as oversights:

- the **291 commits** made before 2026-09-12, which cannot change now without
  rewriting a history that is already public. The file listing on GitHub
  shows the last commit that touched each path, so those French subjects keep
  showing until each file is touched again;
- **test method names**, French phrases with underscores. They are a
  specification, they are read in test output rather than by a stranger
  arriving on the repository, and renaming nine hundred of them would be
  churn with no reader served;
- **log messages**. A log is a diagnostic tool for whoever runs the
  application, not a shop window, and this one is read by its author;
- **`docs/DECISIONS.md`**, four hundred kilobytes across a hundred and
  forty-nine entries. Its worth is its precision, and a translation would
  lose more of that than it would gain: it is the memory of the project,
  read by whoever maintains it rather than by a visitor. New entries are
  written in French too, so the file stays of a piece;
- the **0.1.0 and 0.2.0 sections of the changelog**, which predate the rule.
  Translating them would rewrite what two releases actually announced, and
  neither was ever published as a release on GitHub, so nobody is reading
  them in the wrong language. Every section from 0.3.0 onward is English.

What the user reads on screen never lives in the code at all: it goes through
`src/DtHub.Core/Localization/Strings*.resx`, in the three languages the
application ships.

**A changelog entry is one line.** The section of a version is published
verbatim as its release note, on GitHub and inside the application's own update
window, where it lands in a single text block. One line per change, saying what
changed and nothing else.

The reason a change was made belongs in `docs/DECISIONS.md`, which exists for
it. Writing it in both places served only one of the two readers.

**Group a long list by what it touches.** Past a handful of lines, a flat list
is read from the top or not at all. Game windows, accounts, phones, guides, the
panel: a bold line above each run, and the reader finds their subject at a
glance. Below four or five lines there is nothing to group, and a heading there
is noise.

**This applies to the notes already published too.** 0.3.0 and 0.4.0 went out at
2256 and 2215 words and were shortened afterwards, on the same GitHub releases,
with `gh release edit`. A release note is not a record of what was said on the
day; it is the page someone lands on, and a page nobody reads to the end
announces nothing. What was said on the day is in the commits.

**Decisions get recorded.** An architecture choice, a trade off, something
given up: that goes in `docs/DECISIONS.md`, in French like the rest of that
file, with what was measured and what was set aside. It is the memory of the
project and it is what keeps the same mistake from being made twice.

## Reporting a problem

The issue form guides you through what is needed. The application can compose
a report for you: the "Report a problem" button in the Windows tab puts it in
the clipboard, already stripped of anything that identifies you. Paste it as
it is.

For a vulnerability, do not go through a public issue: see `SECURITY.md`.
