# Phase 8B validation tools

This directory contains the synthetic sample generator and semantic comparator
used by Phase 8B.7. It is not a public file-format SDK.

Generate a fresh set of synthetic samples:

```powershell
dotnet run --project .\eng\phase8b\FurinaChronicle.Phase8BValidation -- generate .\artifacts\phase8b7\samples
```

Inspect a file through the production Portable reader:

```powershell
dotnet run --project .\eng\phase8b\FurinaChronicle.Phase8BValidation -- inspect <portable.json>
```

Compare a source file and a device re-export:

```powershell
dotnet run --project .\eng\phase8b\FurinaChronicle.Phase8BValidation -- compare <source.json> <reexported.json>
```

Create a consistent SQLite backup for the Windows application-upgrade check.
The destination must not already exist:

```powershell
dotnet run --project .\eng\phase8b\FurinaChronicle.Phase8BValidation -- backup <source.db3> <validation-directory\furinachronicle-validation.db3>
$env:FURINA_PHASE8B7_DIRECTORY = '<absolute-validation-directory>'
```

Start a Windows build made with `Phase8BValidationMode=true` from that same
terminal. Only the copied database is upgraded. The environment override is
accepted only in validation builds and must be an absolute directory. Android
continues to use its isolated application-data subdirectory.

Comparison intentionally ignores the target-local archive/account identifiers,
package fingerprint, export time and receiver operation records. It retains
stable fact IDs, account attribution, source identity, timestamps and original
offsets, provenance, completeness, payload and manual revision history.

Generated output belongs under `artifacts/` and is not committed. The generated
manifest records both physical-file SHA-256 and the independent semantic digest;
neither is treated as proof of authorship.
