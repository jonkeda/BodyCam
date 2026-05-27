# M39 — Windows EXE Code Signing

**Status:** Design first
**Goal:** Authenticode-sign the unpackaged Windows `BodyCam.exe` release output
so Windows can verify publisher identity and file integrity, reducing the odds
of Defender/SmartScreen treating it like an unknown unsigned binary.

**Depends on:** M35 (.NET 10 update)

---

## Reality Check

Signing helps, but it is not a magic Defender bypass.

Windows reputation systems look at both the signing publisher and the exact
file hash. A new build can still get an "unrecognized app" warning until the
publisher and/or file gains reputation. A self-signed certificate helps mostly
on machines where that certificate has been manually trusted. For public
distribution, use a trusted code-signing identity such as Microsoft Trusted
Signing / Azure Artifact Signing or a CA-issued OV certificate.

Also, signing is about identity and tamper evidence. It does not suppress a
true malware or PUA detection if Defender dislikes the app's behavior.

This milestone is **not** about Android, iOS, Mac Catalyst, or MSIX. It is only
about signing the Windows unpackaged `.exe` path that BodyCam already uses.

---

## Current State

| Area | State |
|---|---|
| Windows mode | [BodyCam.csproj](../../../src/BodyCam/BodyCam.csproj) sets `<WindowsPackageType>None</WindowsPackageType>` |
| Debug exe found | `src/BodyCam/bin/Debug/net10.0-windows10.0.19041.0/win-x64/BodyCam.exe` |
| Signing tool | `signtool.exe` was not on PATH during inspection; use Visual Studio Developer PowerShell or Windows SDK path |
| Signing asset ignore | `.gitignore` currently ignores `.env`, but not code-signing private keys |

---

## Target Output

Build a Release Windows output folder, then Authenticode-sign:

- `BodyCam.exe`
- any helper `.exe` files in the output folder
- native `.dll` files shipped beside it, especially `webrtc-apm.dll`
- optionally all managed `.dll` files too, for consistency

The primary check is:

```powershell
signtool verify /pa /v .\BodyCam.exe
```

---

## Signing Choices

| Option | Defender/SmartScreen effect | Best use |
|---|---|---|
| Self-signed cert | Only meaningful after the cert is trusted on the machine; public downloads still look unknown | Local/dev/internal testing |
| CA-issued OV cert | Shows a verified publisher; reputation still builds over time | Direct public download |
| Microsoft Trusted Signing / Azure Artifact Signing | Recommended Microsoft path for non-Store Windows distribution | Direct public download and CI |
| Microsoft Store | Best SmartScreen outcome because Store distribution is Microsoft-signed | Public app distribution |

For the immediate goal, start with self-signed local signing if this is only
for your own machine. If you are sharing the app outside your trusted machines,
plan to move to Trusted Signing or an OV certificate.

---

## Phase 1 — Protect Signing Secrets

**Goal:** Keep private keys out of Git before generating anything.

Add to `.gitignore`:

```gitignore
# Code signing secrets
*.pfx
*.p12
*.pvk
.my/signing/**/*.txt
```

Suggested local layout:

```text
.my/signing/windows/
├── bodycam-dev.pfx      # private key backup; never commit
├── bodycam-dev.cer      # public certificate; safe to share for internal trust
└── signing-password.txt # optional local secret file; never commit
```

### Acceptance

- Private key files are ignored.
- `.my/signing/windows/` exists locally when signing is used.

---

## Phase 2 — Create a Local Code-Signing Certificate

**Goal:** Create a self-signed Authenticode certificate for local signing.

Run in PowerShell:

```powershell
New-Item -ItemType Directory -Force .my\signing\windows | Out-Null

$cert = New-SelfSignedCertificate `
  -Type CodeSigningCert `
  -Subject "CN=BodyCam Dev" `
  -FriendlyName "BodyCam dev code signing" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -KeyExportPolicy Exportable `
  -HashAlgorithm SHA256

Export-Certificate `
  -Cert $cert `
  -FilePath .my\signing\windows\bodycam-dev.cer
```

Optional private-key backup:

```powershell
$password = Read-Host "PFX password" -AsSecureString

Export-PfxCertificate `
  -Cert $cert `
  -FilePath .my\signing\windows\bodycam-dev.pfx `
  -Password $password
```

### Trust it on your own test machine

For a self-signed cert, trust is local. Run elevated PowerShell:

```powershell
Import-Certificate `
  -FilePath .my\signing\windows\bodycam-dev.cer `
  -CertStoreLocation "Cert:\LocalMachine\Root"

Import-Certificate `
  -FilePath .my\signing\windows\bodycam-dev.cer `
  -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher"
```

### Acceptance

- Certificate exists in `Cert:\CurrentUser\My`.
- Public certificate is exported to `.my/signing/windows/bodycam-dev.cer`.
- Local machine trusts the certificate for test execution.

---

## Phase 3 — Build the Unpackaged Windows EXE

**Goal:** Produce the Release executable folder without switching the project
to MSIX packaging.

```powershell
dotnet publish src\BodyCam\BodyCam.csproj `
  -f net10.0-windows10.0.19041.0 `
  -c Release `
  -p:WindowsPackageType=None `
  -p:RuntimeIdentifier=win-x64
```

Expected output shape:

```text
src/BodyCam/bin/Release/net10.0-windows10.0.19041.0/win-x64/
```

If `publish` places files under a nested `publish/` folder, sign the files in
that final distribution folder.

### Acceptance

- `BodyCam.exe` exists in the Release Windows output.
- The app still runs before signing, so signing is not hiding a broken build.

---

## Phase 4 — Sign the EXE and PE Payloads

**Goal:** Sign the executable output with SHA-256 and a timestamp.

Run from the folder that contains `BodyCam.exe`:

```powershell
$cert = Get-ChildItem "Cert:\CurrentUser\My" |
  Where-Object Subject -eq "CN=BodyCam Dev" |
  Sort-Object NotAfter -Descending |
  Select-Object -First 1

signtool sign `
  /fd SHA256 `
  /td SHA256 `
  /tr http://timestamp.digicert.com `
  /sha1 $cert.Thumbprint `
  .\BodyCam.exe
```

To sign every executable and DLL in the output folder:

```powershell
$files = Get-ChildItem -File |
  Where-Object Extension -in ".exe", ".dll"

signtool sign `
  /fd SHA256 `
  /td SHA256 `
  /tr http://timestamp.digicert.com `
  /sha1 $cert.Thumbprint `
  $files.FullName
```

Use the trusted CA or Trusted Signing certificate thumbprint instead of the
self-signed thumbprint when moving beyond local/internal testing.

### Acceptance

- `BodyCam.exe` has an Authenticode signature.
- Signature timestamp is present.
- Re-running the sign step is deterministic enough for local release work.

---

## Phase 5 — Verify the Signature

**Goal:** Fail the release if the signature is invalid or missing.

```powershell
signtool verify /pa /v .\BodyCam.exe

Get-AuthenticodeSignature .\BodyCam.exe |
  Format-List Status,StatusMessage,SignerCertificate,TimeStamperCertificate
```

Expected result:

- `signtool verify` succeeds.
- `Get-AuthenticodeSignature` reports `Status: Valid` on a machine that trusts
  the signing certificate.
- File properties show a Digital Signatures tab for `BodyCam.exe`.

### Acceptance

- Verification succeeds on the build machine.
- Verification succeeds on at least one clean test machine after importing the
  `.cer` into `LocalMachine\Root` and `LocalMachine\TrustedPublisher`.

---

## Phase 6 — Automate

**Goal:** Create one script that builds, signs, verifies, and prints the final
folder.

Proposed script:

```text
scripts/signing/Publish-BodyCamWindowsSignedExe.ps1
```

Script responsibilities:

- Locate `signtool.exe` or explain how to open Visual Studio Developer
  PowerShell.
- Build/publish `net10.0-windows10.0.19041.0` with
  `WindowsPackageType=None`.
- Select the configured signing certificate by subject or thumbprint.
- Sign `*.exe` and selected `*.dll` files.
- Verify `BodyCam.exe`.
- Print the output folder and signer subject.

### Acceptance

- A single command produces a signed Windows folder.
- The script never prints private key passwords.
- The script exits non-zero if signing or verification fails.

---

## Risks and Decisions

| Risk | Impact | Mitigation |
|---|---|---|
| Self-signed cert still triggers public warnings | User may still see Defender/SmartScreen warnings | Use self-signed only for trusted machines; move to Trusted Signing or OV for public sharing |
| No timestamp | Signature can become invalid after cert expiry | Always sign with `/tr` and `/td SHA256` |
| Only signing `BodyCam.exe` | Other PE payloads remain unsigned | Sign helper `.exe` and native `.dll` files too |
| `signtool.exe` missing from PATH | Manual signing fails | Use Developer PowerShell or locate Windows SDK `signtool.exe` |
| Private key committed | Signing identity compromised | Ignore `.pfx`/`.p12` and keep backups outside repo |

---

## Verification Checklist

- [ ] `.gitignore` protects `.pfx`, `.p12`, `.pvk`, and local password files.
- [ ] Local code-signing certificate created or trusted signing identity chosen.
- [ ] Release Windows unpackaged output produced.
- [ ] `BodyCam.exe` signed with SHA-256.
- [ ] Timestamp added.
- [ ] `signtool verify /pa /v BodyCam.exe` succeeds.
- [ ] `Get-AuthenticodeSignature BodyCam.exe` reports `Valid` on trusted test
      machines.
- [ ] Signing script added after the manual flow is proven.

---

## References

- Microsoft Learn: SmartScreen reputation for Windows app developers
  — https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- Microsoft Learn: Code signing options for Windows app developers
  — https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- Microsoft Learn: SignTool.exe
  — https://learn.microsoft.com/en-us/dotnet/framework/tools/signtool-exe
- Microsoft Learn: Authenticode digital signatures
  — https://learn.microsoft.com/en-au/windows-hardware/drivers/install/authenticode
