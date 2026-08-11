<div align="center">

# Deadlock Skybox Selector

### Change the sky. Keep the game untouched.

A transparent, reproducible skybox manager for Deadlock with **32 custom skies**, instant Vanilla restore, integrity checks, and automatic backups.

<p>
  <img src="https://img.shields.io/badge/SKYBOXES-32-CB6B4F?style=for-the-badge" alt="32 skyboxes">
  <img src="https://img.shields.io/badge/ANIME-13-3E94B8?style=for-the-badge" alt="13 anime skyboxes">
  <img src="https://img.shields.io/badge/REALISTIC-19-7B8B63?style=for-the-badge" alt="19 realistic skyboxes">
  <img src="https://img.shields.io/badge/INTEGRITY-SHA--256-51596B?style=for-the-badge" alt="SHA-256 verified">
</p>

<img src="./unpacked/assets/previews/anime/anime_05.jpg" width="100%" alt="Deadlock with Anime 05 skybox">

<br>

[**Download the latest release**](https://github.com/harritoncloud/deadlock-skybox-selector/releases/latest) · [Browse all skyboxes](#skybox-gallery) · [Build from source](#build-from-source)

</div>

---

## What it does

The selector replaces only Deadlock's sky material through a managed VPK override. It does not patch game binaries, launch the game, or leave a background process running.

<table>
  <tr>
    <td width="33%"><strong>32 custom skies</strong><br>13 Anime and 19 Realistic variants, including Half-Life 2 Style.</td>
    <td width="33%"><strong>Safe switching</strong><br>Existing managed files are backed up before every change.</td>
    <td width="33%"><strong>One-click Vanilla</strong><br>Restore the original Deadlock sky without touching unrelated addons.</td>
  </tr>
  <tr>
    <td><strong>Verified assets</strong><br>SHA-256 checks protect the archive, runtime, and every extracted VPK.</td>
    <td><strong>Portable release</strong><br>The complete selector can be distributed as one Windows executable.</td>
    <td><strong>Auditable source</strong><br>The launcher, selector, installer, configuration, and assets are all visible here.</td>
  </tr>
</table>

## Quick start

1. Download `SkyboxSelector.exe` from [Releases](https://github.com/harritoncloud/deadlock-skybox-selector/releases).
2. Run the selector and approve the Windows permission request when Deadlock is installed under `Program Files`.
3. Let the first launch prepare and verify the local skybox cache.
4. Open the Anime or Realistic preview sheet and select a skybox.
5. Select **Vanilla** whenever you want to restore the original sky.

The managed cache is stored in `<Deadlock>/patchwin.cc-skyboxes`. Only the selected override is copied into `game/citadel/addons`.

## Skybox gallery

### Anime collection

13 bright, illustrated skies ranging from soft sunsets to saturated blue cityscapes.

<a href="./unpacked/assets/previews/anime-contact-sheet.jpg">
  <img src="./unpacked/assets/previews/anime-contact-sheet.jpg" width="100%" alt="All 13 Anime skyboxes">
</a>

### Realistic collection

19 grounded lighting and weather variants, including warm sunsets, overcast scenes, night skies, and **Half-Life 2 Style**.

<a href="./unpacked/assets/previews/realistic-contact-sheet.jpg">
  <img src="./unpacked/assets/previews/realistic-contact-sheet.jpg" width="100%" alt="All 19 Realistic skyboxes">
</a>

## Safety model

| Protection | Behavior |
| --- | --- |
| Asset validation | Every source VPK is CRC-checked during packaging and SHA-256 checked at runtime. |
| Path confinement | Cache and addon operations are restricted to known managed directories. |
| Backups | Existing selector-managed files are backed up before replacement. |
| Conservative cleanup | Vanilla mode removes only the known managed skybox override. |
| Cache recovery | An incomplete cache is preserved under an `.invalid-*` name instead of being destroyed. |
| Process safety | The selector waits for Deadlock and Deadlock Mod Manager to close before changing files. |

## Build from source

### Requirements

- Windows 10 or Windows 11
- Windows PowerShell 5.1
- .NET Framework C# compiler included with Windows
- Git LFS for cloning the VPK assets

Clone the repository with LFS assets:

```powershell
git lfs install
git clone https://github.com/harritoncloud/deadlock-skybox-selector.git
cd deadlock-skybox-selector
git lfs pull
```

Verify all source and asset hashes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
```

Build the one-file release:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1
```

The finished application is written to `dist/SkyboxSelector.exe`.

## Project layout

<details>
<summary><strong>Open repository map</strong></summary>

| Path | Purpose |
| --- | --- |
| `source/launcher` | C# source for the one-file launcher and resource extractor. |
| `source/gameinfo-installer` | C# source for the permission-aware GameInfo installer. |
| `source/runtime` | Interactive CMD and PowerShell selector interface. |
| `source/config` | GameInfo payload embedded in the installer. |
| `unpacked/runtime` | Exact runtime resources extracted from the release executable. |
| `unpacked/assets` | All previews, manifests, and 32 unpacked skybox VPK files. |
| `unpacked/config` | GameInfo extracted from the nested installer. |
| `tools` | Reproducible unpack, verify, and build scripts. |
| `docs` | Integrity and reproducibility reports. |

</details>

## Credits

Skybox assets are based on **HyperLine's Skybox Replacement v2.0** for Deadlock. Package inspection and VPK validation use **ValveResourceFormat / Source 2 Viewer**. Full attribution is available in [CREDITS.md](./CREDITS.md).

The skybox assets are third-party mod content. This repository does not assert a blanket license over those assets; confirm redistribution permission before publishing or mirroring them.

---

<div align="center">

Built for players who want a different atmosphere without turning installation into guesswork.

</div>

