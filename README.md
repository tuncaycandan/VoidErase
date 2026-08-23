# VoidErase

Secure Windows file/folder destruction utility.

## Version
Current release: **v1.2.1**

## Features
- AES-256-GCM processing with SHA-256 verification
- File and recursive folder selection
- Explorer context menu integration for files and folders
- TR / EN support
- Compact Light UI
- Configurable settings
- Operation summary UI
- GitHub release updater

## Safety
- Windows and Program Files trees are protected by default.
- Source files are deleted only after successful encryption and container verification.
- Failed or inaccessible items prevent unsafe parent-folder cleanup.
- Temporary destruction containers are cleaned up when an operation fails or is cancelled.
- System files are not processed.
- File and folder operations support cancellation without intentionally deleting the original source before verification succeeds.

## ScreenShot
![VoidErase Main Window](ss.PNG)

## Build

VoidErase targets **.NET Framework 4.8** and is built for **x64 Windows**. Open `VoidErase.Framework48.slnx` or `VoidErase.Framework48.csproj` in Visual Studio with the .NET Framework 4.8 Developer Pack installed. Alternatively, run the PowerShell build script from a Windows PowerShell session:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Build-Framework48.ps1
```

The script uses MSBuild when available and falls back to the `dotnet` CLI. It verifies that `bin\\Release\\VoidErase.exe` was produced. The sandbox used for source inspection does not contain the Windows .NET Framework targeting pack, so the final Release build must be verified on Windows.

Historical source copies and patch notes are kept under `archive/` and are not part of the active build.

## GitHub updater
Repository: https://github.com/tuncaycandan/VoidErase
Release asset: `VoidErase.exe`

## License

VoidErase is licensed under the GNU General Public License v3.0 (GPL-3.0).

See the [LICENSE](LICENSE) file for the full license text.
