# VoidErase v1.4.0 — Final Reliability, Security & Performance Update

VoidErase v1.4.0 is a Windows desktop release focused on safer operation feedback, stronger evidence handling, improved performance, and reproducible Release builds. The release preserves the application’s safety boundaries and does not add direct destructive physical-disk write commands.

## Highlights

- Improved operation locking, cancellation feedback, and window-close protection while an operation is running.
- Clearer security-scope explanations in the main window and Settings window.
- Session-aware UTC logging with millisecond timestamps and bounded log rotation.
- Real application log-folder navigation from the Logs button.
- Mandatory SHA-256 verification for downloaded updates before execution.
- Buffered history writes with a final flush at operation completion.
- Reduced UI progress-dispatch pressure and improved progress callback behavior.
- Sequential 1 MB file I/O and reusable cryptographic buffers for lower allocation pressure.
- A Framework 4.8-compatible secure buffer pool that clears sensitive buffers before reuse.
- A safe benchmark mode that performs an encrypt/validate round trip without deleting the source files.
- Automated `Build-Release.ps1` support for clean builds, optional tests, output validation, and SHA-256 reporting.

## Security and Evidence

VoidErase validates the selected target before processing and retains protections for system paths, the running system drive, reparse points, and unsupported or ambiguous media conditions. Operation results, validation states, identity information, and NIST evidence remain explicit in the summary and exported reports.

The application-level encrypt/validate workflow must not be interpreted as proof of physical-media Purge or Destroy. SSD, NVMe, and USB media may require device-specific sanitization capabilities that are outside this release’s authorized execution boundary.

## User Experience

The interface now communicates why an operation is blocked, keeps destructive controls locked during active work, asks for confirmation before closing during an active operation, and presents safer cancellation behavior. Settings and log navigation have been clarified without changing the v1.4 application version.

## Performance and Memory

Large cryptographic buffers are reused through a Framework 4.8-compatible secure pool and are cleared before being returned. Nonce, tag, and fixed-size metadata allocations are reduced in the chunk loops. Sequential file access is enabled for encryption and validation, while progress updates are rate-limited to keep the WinForms UI responsive.

History entries are buffered and flushed in bounded batches. The benchmark mode reports elapsed time, throughput, verification state, and process Private Bytes before and after each file.

## Benchmark Usage

Create or select an existing test directory and run:

```powershell
.\bin\Release\VoidErase.exe --benchmark "C:\VoidErase-Test"
```

The source files in the selected directory are preserved. Temporary benchmark containers are removed after validation, and results are written to:

```text
C:\VoidErase-Test\voiderase-benchmark-results.csv
```

The CSV uses invariant numeric formatting so it remains valid under Turkish, English, and other Windows locale settings.

## Build and Verification

Use the supplied script from the project directory:

```powershell
.\Build-Release.ps1 -Clean
```

The script verifies the project, builds the .NET Framework 4.8 Release configuration, confirms the expected EXE, and prints its size and SHA-256 hash. The final Release configuration is x64 and remains version `1.4.0`.

## Testing

The final validation included a clean Release build, a safe benchmark smoke test with a temporary file, CSV verification, version verification, and output hash verification. No destructive physical-media test was performed.

## Upgrade Notes

Build the Framework 4.8 application on Windows 10 or later with PowerShell and the supplied build script. Existing NIST XML records are not modified. New records use the v1.4.0 application version metadata.
