# VoidErase

Secure Windows file/folder destruction utility.

## Current features
- AES-256-GCM processing with SHA-256 verification
- File and recursive folder selection
- Explorer context menu integration
- TR / EN UI
- Compact Light UI
- Settings
- Operation summary
- Optional local logs
- GitHub release updater

## Safety
Windows and Program Files trees are protected by default. The source file/folder is only removed after successful verification. If a folder contains a failed item, the folder tree is not removed.

## GitHub updater
Repository: https://github.com/tuncaycandan/VoidErase

Release asset name:
`VoidErase.exe`

Versioning is controlled by the project assembly/package version.
