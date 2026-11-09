<!--
  Copyright (c) 2026 DaKang233.
  SPDX-License-Identifier: MIT
-->

# Phase 8C validation tools

`FurinaChronicle.Phase8CValidation` is a host-side, read-only acceptance tool for
Phase 8C.6 when used with generate and compare. Its 8C.7 interruption command
creates and reopens a separately owned synthetic SQLite database.

Generate the two independent synthetic origins:

```powershell
dotnet run --project .\eng\phase8c\FurinaChronicle.Phase8CValidation -- generate windows .\artifacts\phase8c6\windows-origin.fca
dotnet run --project .\eng\phase8c\FurinaChronicle.Phase8CValidation -- generate android .\artifacts\phase8c6\android-origin.fca
```

Compare an origin package with the package exported after a device round trip:

```powershell
dotnet run --project .\eng\phase8c\FurinaChronicle.Phase8CValidation -- compare .\artifacts\phase8c6\windows-origin.fca .\artifacts\phase8c6\windows-returned.fca --report .\artifacts\phase8c6\windows-roundtrip.md
```

The report records:

- container, declared uncompressed and staging sizes;
- sampled temporary-directory and process-working-set peaks;
- elapsed validation time;
- strict logical differences.

Different ZIP hashes are not a failure by themselves. The semantic comparer
ignores target-local parent identifiers, export metadata, and additional
receiving-operation revisions. It still rejects missing source revisions,
changed time precision, merged same-UID archive copies, missing empty parents,
and changed facts.

Generated samples and reports are engineering artifacts, not device evidence.
The Windows and Android product flows must still perform Preview, confirmed
Merge, reopen, repeated NoOp, cancel/refuse, export, and the return comparison.
Replace must use an explicitly selected, disposable validation scope.

Build the product UI against an isolated database:

```powershell
dotnet build .\FurinaChronicle.App\FurinaChronicle.App.csproj `
  --framework net10.0-windows10.0.19041.0 `
  --configuration Release `
  -p:Phase8CValidationMode=true

dotnet build .\FurinaChronicle.App\FurinaChronicle.App.csproj `
  --framework net10.0-android `
  --configuration Release `
  -p:AndroidSdkDirectory=E:\AndroidSDK `
  -p:AndroidPackageFormats=apk `
  -p:EmbedAssembliesIntoApk=true `
  -p:Phase8CValidationMode=true
```

`Phase8CValidationMode` keeps the normal 8C.5 pages and services but changes
the title, Android application ID, and database directory. Its Android package
is `com.companyname.furinachronicle.validation.phase8c6`, so it can be installed
beside the main application. The two validation-mode properties are mutually
exclusive.

Windows validation builds now require an explicit absolute `FURINA_PHASE8C6_DIRECTORY`
before launch (8B uses `FURINA_PHASE8B7_DIRECTORY`). The database stays directly under
that root; metadata, preview data and temporary files use its `app-data/` and `cache/`.
Windows validation Preferences are session-only and credential storage is disabled;
no production Preferences/SecureStorage fallback is allowed. This is a credential-free
regression host, not a login/credential migration test. Android keeps its package-private
platform storage.

When changing `Phase8CValidationMode`, `Phase8BValidationMode` or `Ui1PreviewMode`,
`build/FurinaMauiIdentity.targets` invalidates the generated Windows manifest when
identity inputs change and rejects a mismatched package identity/title. Older revisions
need a clean of the matching TFM/configuration/RID before switching modes. For every
acceptance build, still verify the generated AssemblyInfo and actual DLL identity before
launch; changing only the database path does not establish storage isolation.

The incremental regression deliberately skips cleaning and launches no application:

```powershell
.\eng\ui1\Test-MauiBuildIdentity.ps1 -EvidenceDirectory .\artifacts\ui1\identity-check
```

Use a separate `BaseOutputPath` for validation artifacts so the existing production
binaries remain intact. Do not infer application identity from the apphost EXE hash:
the identity metadata is in `FurinaChronicle.App.dll`.

## Phase 8C.7 process interruption probe

The probe uses the same staged package, planner, atomic store, and Receipt query
as the application. A new run creates its own synthetic database. It adds one
target-only Gacha record, then plans a Gacha-domain Scoped Replace to remove it.
Use one run GUID for all five commands, in order.

| Command | Observable marker and driver action |
|---|---|
| prepare | Wait for PREPARED and allow normal completion. |
| before | Wait for BEFORE_COMMIT and forcibly terminate that process. The transaction has written the facts and Receipt. |
| recover | Wait for ROLLED_BACK and allow completion. It checks the original state token, retained record, absent Receipt, integrity, and orphan cleanup. |
| after | Wait for AFTER_COMMIT and forcibly terminate that process. Apply has returned an Applied Receipt. |
| check | Wait for REOPENED and allow completion. It checks the original Receipt, fingerprints, mappings, recovery point, and idempotent replay. |

On Windows, build the host tool once and start its executable separately for
each command with `interruption <command> <absolute-evidence-directory> <run-guid-N>`.
Terminate only the probe PID identified by the matching marker.

On Android, build the isolated package with the connected device ABI:

```powershell
dotnet build .\eng\phase8c\FurinaChronicle.Phase8CAndroidProbe -c Debug -r android-arm64 -p:AndroidSdkDirectory=E:\AndroidSDK
```

Verify the APK application ID `com.companyname.furinachronicle.validation.phase8c7probe`
before installing. The ADB shell launches its DUMP-protected activity with
`am start -W -n com.companyname.furinachronicle.validation.phase8c7probe/.ProbeActivity --es command <command> --es run <run-guid-N>`.
Filter logcat by tag `Furina8C7Probe`, run GUID and PID. For the three normal
commands, wait for `COMPLETE` before restarting. At the two hold markers,
use `am force-stop` on the exact probe package and verify its PID disappears.
Start each subsequent command in a new process. The probe data stays in
the package-private synthetic directory.
