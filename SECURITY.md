# Report a vulnerability

DT Hub downloads two third-party tools, updates itself from GitHub, and
drives a phone over ADB. If you find something that affects security,
please report it privately rather than in a public issue.

**How.** Open a private security advisory on the repository:
[Security > Report a vulnerability](https://github.com/Falcomfr/DtHub/security/advisories/new).
GitHub keeps it between you and me until a fix exists.

**If this form does not open**, private reporting is not enabled on the
repository. Even so, do not write anything technical in public: open an
ordinary issue saying only that you found something that affects
security, without describing it, and I will open the private channel. A
security policy that points to a closed door is worse than no policy at
all, hence this fallback route.

**What helps.** The version shown by the application, what happens, and
what it takes to reproduce it. The application's diagnostic report is
enough: it is already redacted of anything identifying.

**What you can expect.** A reply within a week. DT Hub is written by one
person in their spare time: there is no on-call duty, no bug bounty, and
no promised fix deadline.

## What counts as a vulnerability here

- A way to make DT Hub execute code, or to make it place a file it has
  not verified.
- A bypass of the setup checks: hash, size, HTTPS.
- Personal data that survives the diagnostic report's redaction, or that
  reaches the logs when it should not. Pairing codes are included.
- An escape from the sandboxing of the guides window, which must never
  leave papycha.fr nor open anything other than HTTPS.

## What does not count as one

- The SmartScreen warning on first launch. The binary is not signed and
  `docs/CONFIANCE.md` explains why, and what it would take to fix that.
- The fact that the application queries GitHub on every startup. This is
  stated in the README, and nothing is installed without being asked
  for.
- An antivirus that flags an unsigned, self-extracting .NET executable.
  This is expected, it is documented, and the answer is never to add an
  exclusion.
