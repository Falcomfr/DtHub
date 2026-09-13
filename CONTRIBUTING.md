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

Two things stay as they are, and it is better to say so than to hide it:

- the **291 commits** made before 2026-09-12 are in French, and cannot change
  now without rewriting a history that is already public. The file listing on
  GitHub shows the last commit that touched each path, so those French
  subjects keep showing until each file is touched again;
- **test method names** are French phrases with underscores, and they are not
  renamed. They are a specification, they are read in test output rather than
  by a stranger arriving on the repository, and renaming nine hundred of them
  would be churn with no reader served;
- **log messages** stay French. A log is a diagnostic tool for whoever runs
  the application, not a shop window, and this one is read by its author.

What the user reads on screen never lives in the code at all: it goes through
`src/DtHub.Core/Localization/Strings*.resx`, in the three languages the
application ships.

**Decisions get recorded.** An architecture choice, a trade off, something
given up: that goes in `docs/DECISIONS.md`, with what was measured and what
was set aside. It is the memory of the project and it is what keeps the same
mistake from being made twice.

## Reporting a problem

The issue form guides you through what is needed. The application can compose
a report for you: the "Report a problem" button in the Windows tab puts it in
the clipboard, already stripped of anything that identifies you. Paste it as
it is.

For a vulnerability, do not go through a public issue: see `SECURITY.md`.
