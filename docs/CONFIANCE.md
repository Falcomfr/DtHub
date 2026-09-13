# Getting the application recognized

DT Hub is a sixty megabyte executable, distributed outside the stores,
that updates itself and downloads two third party tools. Each of these
traits is, taken on its own, also what malicious software does. This
document states what is already done, what is missing, and in what order
to address it.

**What will never be done:** adding an exclusion to Windows Defender, or
asking someone to do so. An application that needs the antivirus disarmed
is not a trustworthy application, it is an application asking for a
privilege.

## Where things stand, measured

| Point | State |
|---|---|
| Defender scan of the published binary | No threat |
| Product name in the file | "DT Hub", matching what the window says |
| Description in the file | A sentence, not a file name |
| Publisher | Falcomfr |
| Authenticode signature | **Absent** |
| SmartScreen reputation | **None**, the file never having been distributed |

The identity check is replayed on every release by the pipeline, which
refuses to ship a binary missing its name, description or publisher. That
is how it was caught still announcing "DT Touch" weeks after the rename.

## What will trigger alerts, and why

**The lack of a signature, first.** Windows SmartScreen warns on any
downloaded executable it does not know, signed or not; but a signed binary
accumulates its reputation on the certificate, and therefore across all
its versions at once, whereas an unsigned binary starts from zero at every
release. Without a signature, the "Windows protected your PC" warning will
keep coming back.

**Automatic updates.** Downloading an executable and replacing one's own
is the behavior of a payload installer. What sets this one apart: the
address is that of a public repository, the checksum is verified before
the file is used, and nothing runs before the next launch. This will not
convince a heuristic, it will convince an analyst.

It is worth stating what this checksum guarantees and what it does not:
it is read from the `.sha256` file of the **same** release. It therefore
protects against transport, against a download truncated or altered in
transit. It does not protect against the publication of a fake release,
which would carry its own checksum. What protects against that is the
public code, and the signature once it exists.

**Downloading ADB and scrcpy.** Nineteen megabytes of third party tools,
taken from official sources, checksums verified, addresses centralized in
`build/dependencies.json`. An analyst will check this file. The user, for
their part, sees at first launch a window naming each component, its
publisher and the address it comes from, then shows the download and the
verification.

**The single self-extracting file.** An executable that decompresses
itself looks like a packed binary. This is the normal shape of a
self-contained .NET application, and the engines know it, but it still
counts toward a score.

## What remains to be done, in order of effect

### 1. Signing, which helps, but not the way people think

**No certificate silences SmartScreen on day one.** This is the first
thing to know, and it contradicts what most resellers still sell.
Verified in September 2026.

The EV certificate long granted reputation from the start. Microsoft
changed this behavior in March 2024: EV remains the highest level of
verification certificate, required for drivers, but it no longer buys
SmartScreen's silence. Paying the premium for that reason alone no longer
makes sense.

What signing actually buys, and what is worth the price:

- the publisher's name in place of "Unknown publisher" in the warning;
- a reputation that **accumulates from one version to the next** instead
  of starting from zero at every release, which is the fate of an
  unsigned binary.

Three routes, to be rechecked with the provider since conditions move
fast.

**SignPath Foundation, free, and the route to look at first.** It signs
open source projects for free, with Sectigo OV level certificates. Its
mechanism is more demanding than buying a certificate, and that is what
gives it its value: it verifies that the binary was indeed built from the
public repository, and stakes its name on it.

Two conditions currently block this, and both already appear in what
remains to be done: the repository must be **public**, and the project
must **already have a release** in the form to be signed. The MIT license
qualifies. Reviewing the application takes anywhere from a few days to a
few weeks.

One point to check in their terms before committing: the publisher shown
is the foundation's, which vouches for the project, and not "Falcomfr".
That is a choice, not a detail.

The other two routes, paid:

**Azure Artifact Signing**, from Microsoft, renamed in 2026 and formerly
Trusted Signing. About ten dollars a month for five thousand signatures,
with no hardware token since the key stays with them. Open to businesses
and verified **independents** from the European Union, the United
Kingdom, the United States and Canada: that is the first condition to
check, an individual without a registered status does not qualify. Expect
a few business days of identity verification.

A serious caveat, noted in 2026: Microsoft rotates its intermediate
authorities, and several users see the SmartScreen warning reappear at
every release because the new authority does not yet have a reputation.
The reputation of a binary signed with them is therefore never acquired
once and for all.

**An OV or EV certificate** from a certificate authority. From two
hundred fifty to six hundred dollars a year, on a hardware token or in a
cloud vault, software keys no longer being accepted. Since February 2026,
validity is capped at one year, which makes annual renewal mandatory.
Reputation then builds up over a few weeks and a few hundred downloads.

Once the certificate is obtained, nothing to change in the repository:
set a `SIGNING_COMMAND` secret on the GitHub repository, containing the
provider's full signing command line. The pipeline runs it on the
published binary, verifies that the signature took, and refuses to ship
if it did not. Without this secret, the step is skipped and the release
ships unsigned.

### 2. Verify before shipping

Before the first release, send the binary to VirusTotal and see what the
sixty engines say about it. **Warning: sending a file to VirusTotal
publishes it**, making it accessible to the service's subscribers. This
is not a problem for an application whose code is public, but it would be
one for a private binary.

One or two marginal engines crying wolf over an unsigned .NET binary is
unremarkable. A dozen, or a major engine, deserves to be understood
before releasing.

### 3. Getting a false alert lifted

If an engine is wrong, every vendor has a submission form. For Microsoft,
it is the "Submit a file for analysis" portal of the security center,
choosing "False positive". The turnaround is a few days. Attach the link
to the repository and to the release: public source code is the most
effective argument.

### 4. What helps at no cost

- Shipping from the repository, with the checksum next to the binary,
  which is already the case. Someone must be able to verify what they
  downloaded.
- Keeping the code public: that is what distinguishes a false alert from
  a real doubt.
- Not changing the file name from one version to the next, since
  reputation attaches to it.
- Never asking for administrator rights, which the application does not
  do and must not start doing.

## Checking a binary's state yourself

```powershell
# Identité et signature
$f = 'chemin\DtHub.exe'
(Get-Item $f).VersionInfo | Format-List ProductName, FileDescription, CompanyName, FileVersion
Get-AuthenticodeSignature $f | Format-List Status, SignerCertificate

# Defender scan, without changing any of its settings
& 'C:\Program Files\Windows Defender\MpCmdRun.exe' -Scan -ScanType 3 -File $f
```
