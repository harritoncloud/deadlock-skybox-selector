<div align="center">

# Deadlock Skybox Selector

### A polished one-file skybox manager for Deadlock

Choose from **32 custom skies** in a native Windows GUI, switch safely, and restore Vanilla at any time.

<p>
  <img src="https://img.shields.io/badge/SKYBOXES-32-D99A4E?style=for-the-badge" alt="32 skyboxes">
  <img src="https://img.shields.io/badge/INTERFACE-WINFORMS-5AA89C?style=for-the-badge" alt="WinForms GUI">
  <img src="https://img.shields.io/badge/INTEGRITY-SHA--256-7C8B72?style=for-the-badge" alt="SHA-256 verified">
  <img src="https://img.shields.io/badge/PLATFORM-WINDOWS-BC6F7F?style=for-the-badge" alt="Windows">
</p>

<img src="./unpacked/assets/previews/anime/anime_05.jpg" width="100%" alt="Deadlock with the Azure City skybox">

<br>

[**Download the latest release**](https://github.com/harritoncloud/deadlock-skybox-selector/releases/latest) &middot; [View the gallery](#gallery) &middot; [Build from source](#build-from-source)

</div>

---

## Highlights

| Feature | Behavior |
| --- | --- |
| Modern fixed-size GUI | Custom dark Deadlock-inspired interface with animated cards and smooth scrolling. |
| 32 named skyboxes | Every card has a readable atmosphere-based name and an image preview. |
| In-place switching | Applying a skybox updates the current window without restarting the application. |
| Safe override | Unknown files occupying the skybox slot are SHA-256 verified and backed up before replacement. |
| Vanilla restore | Removes only the managed skybox override and leaves unrelated addons untouched. |
| First-run setup | A consent screen and progress loader prepare the verified local library without a console window. |
| Optional FPS profile | Adds a managed block to `autoexec.cfg`, preserving existing user settings and creating a backup. |
| GameInfo installer | Mounts `citadel/addons`, keeps client physics enabled, and creates a verified backup before replacement. |

The selector does not launch Deadlock and does not remain active after its window is closed.

## Quick start

1. Download `SkyboxSelector.exe` from [Releases](https://github.com/harritoncloud/deadlock-skybox-selector/releases).
2. Close Deadlock and any Deadlock mod manager.
3. Run the selector and approve Windows elevation if the game is under `Program Files`.
4. Approve the first-run library installation.
5. Select a card and press **Apply**.
6. Press **Restore** to return to the original Deadlock skybox.

The verified library is stored in `<Deadlock>/dlskybox`. Legacy `deadlockcustomskybox` and `patchwin.cc-skyboxes` caches are migrated automatically.

## Gallery

The GUI presents one unified library. The source assets retain their original internal groups only for packaging and attribution.

<a href="./unpacked/assets/previews/anime-contact-sheet.jpg">
  <img src="./unpacked/assets/previews/anime-contact-sheet.jpg" width="100%" alt="Skybox gallery sheet one">
</a>

<a href="./unpacked/assets/previews/realistic-contact-sheet.jpg">
  <img src="./unpacked/assets/previews/realistic-contact-sheet.jpg" width="100%" alt="Skybox gallery sheet two">
</a>

## Safety model

| Protection | Implementation |
| --- | --- |
| Asset integrity | The embedded archive, runtime helpers, and all 32 VPK files are checked with SHA-256. |
| Path confinement | Cache, backup, and addon operations are restricted to validated child paths. |
| Transactional switching | Sources are verified before copying; failed changes roll back to the previous verified file. |
| Unknown-mod preservation | An unfamiliar `pak01_dir.vpk` is copied to a timestamped backup and verified before override. |
| Process guard | Skybox and GameInfo changes are blocked while Deadlock or supported mod managers are running. |
| Cache recovery | Invalid caches are quarantined under an `.invalid-*` name rather than deleted. |
| Config preservation | The FPS profile owns only a marked block and leaves all other `autoexec.cfg` content intact. |

## Build from source

### Requirements

- Windows 10 or Windows 11
- Windows PowerShell 5.1
- .NET Framework C# compiler included with Windows
- Git LFS

```powershell
git lfs install
git clone https://github.com/harritoncloud/deadlock-skybox-selector.git
cd deadlock-skybox-selector
git lfs pull
```

Verify the readable and extracted source trees:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
```

Build the one-file GUI:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1
```

The result is written to `dist/SkyboxSelector.exe`.

### Tests

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\fps-config.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\onefile-integration.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\test-startup.ps1
```

`tests/first-run.ps1` performs a full archive extraction and cache-migration test. The UI benchmark and in-place Apply test use an installed Deadlock cache.

## Repository layout

| Path | Purpose |
| --- | --- |
| `source/launcher` | One-file launcher, WinForms GUI, manifest, and application icon. |
| `source/gameinfo-installer` | Permission-aware GameInfo installer source. |
| `source/runtime` | Skybox transaction logic, optional FPS profile, and compatibility command wrapper. |
| `source/config` | GameInfo payload embedded in the installer. |
| `unpacked/runtime` | Exact runtime resources extracted from the release executable. |
| `unpacked/assets` | Preview images, manifest, and 32 unpacked VPK files. |
| `unpacked/config` | GameInfo extracted from the nested installer. |
| `tests` | Isolated first-run, switching, rollback, backup, and FPS-profile tests. |
| `tools` | Reproducible build, unpack, verification, startup, and UI benchmark scripts. |

## Credits

Skybox assets are based on **HyperLine's Skybox Replacement v2.0** for Deadlock. Package inspection and VPK validation use **ValveResourceFormat / Source 2 Viewer**. See [CREDITS.md](./CREDITS.md) for attribution.

The skybox assets are third-party mod content. Confirm redistribution permission before publishing mirrors or derivative packages.

---

<div align="center">

Made by **harriton**

</div>
