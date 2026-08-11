# Deadlock Skybox Selector

Readable source tree and unpacked resources for the one-file Deadlock Skybox
Selector.

## Repository layout

- `source/launcher` - source of the one-file launcher.
- `source/gameinfo-installer` - source of the embedded GameInfo installer.
- `source/runtime` - command and PowerShell selector UI.
- `source/config` - GameInfo payload embedded in the installer.
- `unpacked/runtime` - exact runtime resources extracted from the EXE.
- `unpacked/assets` - the embedded `skyboxes.7z`, fully extracted.
- `unpacked/config` - GameInfo extracted from the nested installer.
- `tools` - extraction, integrity verification, and standalone build scripts.

The original `SkyboxSelector.exe` is intentionally not stored here. It is over
GitHub's normal per-file limit and can be reproduced with `tools/build.ps1`.

## Large files

The unpacked VPK files use Git LFS. Install Git LFS before the first commit:

```powershell
git lfs install
git lfs track "*.vpk"
```

The included `.gitattributes` already contains the required VPK rule.

## Verify

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
```

Verification checks all 32 VPK files against `manifest.json`, compares the
readable sources with their extracted copies, validates the embedded GameInfo,
and reports repository size limits.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1
```

The result is written to `dist/SkyboxSelector.exe`. Building requires Windows,
Windows PowerShell 5.1, and the .NET Framework C# compiler included with Windows.
The required 7-Zip runtime is preserved under `unpacked/runtime`.

## Asset notice

The Anime and Realistic skybox assets are third-party mod content. See
`CREDITS.md`. No blanket license for those assets is asserted by this source
tree; confirm redistribution permission before publishing them.

