# VoidErase v1.4.0 — Evidence, Identity & UX Update

This release improves media identity validation, NIST reporting, dry-run provider traceability, error visibility, settings usability, and interface consistency while preserving the safe application-level processing boundary.

## Included improvements

The application captures pre-operation and post-operation media identities when available and compares the physical drive path, disk number, model, serial number, media type, bus type, and media size. Identity results are stored in NIST XML records and displayed in HTML reports and operation summaries.

NIST reports now show identity status, identity match state, pre-operation and post-operation identity snapshots, and provider version. The report summary remains explicit that application-level processing and verification do not claim physical-media Purge or Destroy.

The dry-run provider contract exposes provider version and physical-write authorization state. The built-in provider remains dry-run only and always reports that physical device writes are not authorized.

The Settings window is compacted, the Delete hidden files option is visible and persisted, and the language selector is narrow and aligned next to the Language label. Context-menu status uses a green check when enabled and a red cross when disabled.

Dry-run tests cover system and boot-disk blocking, identity mismatch detection, and provider write authorization. No zero-fill, secure-erase, TRIM, firmware, `clean`, or other irreversible physical-device command is included.

## Build on Windows

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Build-Framework48.ps1
```

The sandbox could not execute the Windows .NET Framework 4.8 GUI build because no Windows/.NET Framework build toolchain is available there. Run the supplied build script on Windows 10 or later before publishing the v1.4.0 binary.
