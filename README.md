# VoidErase

Secure Windows file/folder destruction utility.

## Version
Current release: **v1.1.0**

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

## GitHub updater
Repository: https://github.com/tuncaycandan/VoidErase
Release asset: `VoidErase.exe`

## License

VoidErase is licensed under the GNU General Public License v3.0 (GPL-3.0).

See the [LICENSE](LICENSE) file for the full license text.
