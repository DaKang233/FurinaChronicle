<!--
  Copyright (c) 2026 DaKang233.
  SPDX-License-Identifier: MIT
-->

# Phase 8C validation tools

`FurinaChronicle.Phase8CValidation` is a host-side, read-only acceptance tool for
Phase 8C.6. It does not open a FurinaChronicle production database.

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
