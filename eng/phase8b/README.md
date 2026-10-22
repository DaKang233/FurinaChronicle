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

Comparison intentionally ignores the target-local archive/account identifiers,
package fingerprint, export time and receiver operation records. It retains
stable fact IDs, account attribution, source identity, timestamps and original
offsets, provenance, completeness, payload and manual revision history.

Generated output belongs under `artifacts/` and is not committed. The generated
manifest records both physical-file SHA-256 and the independent semantic digest;
neither is treated as proof of authorship.
