<div align="center">

# Deadlock Skybox Selector

### 32 skies. One clean selector.

A polished one-file Windows application for changing the atmosphere of Deadlock safely and restoring Vanilla at any time.

<p>
  <img src="https://img.shields.io/badge/SKYBOXES-32-D99A4E?style=for-the-badge" alt="32 skyboxes">
  <img src="https://img.shields.io/badge/INTERFACE-WINFORMS-5AA89C?style=for-the-badge" alt="Native Windows GUI">
  <img src="https://img.shields.io/badge/INTEGRITY-SHA--256-7C8B72?style=for-the-badge" alt="SHA-256 verified">
  <img src="https://img.shields.io/badge/PLATFORM-WINDOWS-BC6F7F?style=for-the-badge" alt="Windows">
</p>

<img src="./unpacked/assets/previews/anime/anime_05.jpg" width="100%" alt="Azure City skybox in Deadlock">

<br>

[**Download the latest release**](https://github.com/harritoncloud/deadlock-skybox-selector/releases/latest) &middot; [View all skyboxes](#skybox-library) &middot; [Read the safety model](#safety-model)

</div>

---

## Highlights

| Feature | Behavior |
| --- | --- |
| Native fixed-size GUI | Custom Deadlock-inspired interface with animated cards and high-refresh smooth scrolling. |
| 32 named skyboxes | Every card uses its current atmosphere name and a matching preview. |
| In-place switching | **Apply** changes the selected skybox without restarting the application. |
| Safe override | Unknown skybox mods are verified, backed up, and then replaced transactionally. |
| Vanilla restore | **Restore** removes only the managed override and leaves unrelated addons untouched. |
| Eight visual fixes | A dedicated **Fixes** page controls base veil, factory smoke, unit names, the floating pickup book, player markers, HP-bar tick marks, classic ability fill, and ColorFix independently. |
| Clean first run | Consent and loading screens prepare the local library without showing a console window. |
| Saved GameInfo | The GameInfo page carries the exact configuration installed on 30 September 2026 and applies it only on request, with a backup. |

The window can be moved from any non-interactive surface, and the greeting automatically uses the current Windows account name.

## Quick Start

1. Download `SkyboxSelector.exe` from [Releases](https://github.com/harritoncloud/deadlock-skybox-selector/releases/latest).
2. Close Deadlock and any Deadlock mod manager.
3. Run the selector and approve Windows elevation if Deadlock is installed under `Program Files`.
4. Approve the first-run library installation.
5. Select a skybox card and press **Apply**.
6. Press **Restore** whenever you want to return to the original Deadlock skybox.

On the **Fixes** page, each row has a switch showing whether that visual fix is active. Close Deadlock before switching. A slot occupied by another mod is reported and never overwritten.

**Base Veil** removes the large team-colored clouds without changing gameplay fog or the selected skybox (`pak03_dir.vpk`). **Factory Smoke** removes the factory's large black pollution cloud (`pak04_dir.vpk`).

**Unit Names** uses the [Hide Names mod](https://gamebanana.com/mods/722348) (`pak05_dir.vpk`); it keeps health bars and names printed on map geometry. **Pickup Book** (`pak06_dir.vpk`) hides the brief model above the player after collecting a bonus. That gained-effect model is shared by several pickups, so their floating models may also disappear; the world pickups, reward, beam, and glow remain.

**Player Markers** (`pak07_dir.vpk`) hides the in-world player-name, portrait, and distance indicator. It leaves the separate object indicators and minimap alone. The override patches only the `PlayerEntry.visible` rule in Deadlock's compiled `hud_unit_indicators_v2` stylesheet.

**HP Bar Lines** (`pak08_dir.vpk`) hides the horizontal large and small tick marks inside the player's health bar. The health fill, border, and numeric values remain. The override changes only the two `#healthLines` opacity rules in Deadlock's compiled `hud_health` stylesheet.

**Classic Ability Fill** (`pak09_dir.vpk`) restores a brighter purple fill and readable lavender checkmarks for purchased ability-upgrade rows, instead of the newer green fill. The AP counter is purple too; upgrade state is unchanged.

**ColorFix** (`pak10_dir.vpk`) softens the scene's contrast through the game's post-processing profiles. It does not alter the HUD and can be switched off independently to restore the original look.

The **GameInfo** page holds an exact snapshot of the current installed `gameinfo.gi` (SHA-256 `78695F98DC3FE3C2C4824DF769D8FFB1879F27DDA430CCF3B2E0A7C4B22DBC46`). **Apply Config** can reinstall this snapshot at any time, even when it already matches the game file. The file being replaced is backed up first. There is no GameInfo Restore button in the interface; backups remain on disk.

The verified library is stored in `<Deadlock>/dlskybox`. Older `deadlockcustomskybox` and `patchwin.cc-skyboxes` caches are migrated automatically.

## Skybox Library

The application presents a single unified library. These are the current names shown on the cards:

| # | Skybox | # | Skybox |
| ---: | --- | ---: | --- |
| 01 | Golden Citadel | 17 | Morning Glow |
| 02 | Amber Rooftops | 18 | Golden Hour |
| 03 | Quiet Morning | 19 | White Haze |
| 04 | Soft Sunrise | 20 | Cloudbreak |
| 05 | Azure City | 21 | Pale Noon |
| 06 | Golden Clouds | 22 | Clear Day |
| 07 | Clear Horizon | 23 | High Clouds |
| 08 | Blue Evening | 24 | Storm Light |
| 09 | Starlit Night | 25 | Grey Front |
| 10 | Mountain Air | 26 | Blue Skies |
| 11 | Cotton Candy | 27 | Rainy Sunset |
| 12 | Crystal Sky | 28 | Ember Sunset |
| 13 | Bright Downtown | 29 | Fading Day |
| 14 | Silver Overcast | 30 | City Mist |
| 15 | Burnished Gold | 31 | Deep Fog |
| 16 | Rose Dusk | 32 | Nightlock |

### Preview Sheet 01

Golden Citadel through Bright Downtown.

<a href="./unpacked/assets/previews/anime-contact-sheet.jpg">
  <img src="./unpacked/assets/previews/anime-contact-sheet.jpg" width="100%" alt="Golden Citadel through Bright Downtown skybox previews">
</a>

### Preview Sheet 02

Silver Overcast through Nightlock.

<a href="./unpacked/assets/previews/realistic-contact-sheet.jpg">
  <img src="./unpacked/assets/previews/realistic-contact-sheet.jpg" width="100%" alt="Silver Overcast through Nightlock skybox previews">
</a>

## Safety Model

| Protection | Implementation |
| --- | --- |
| Asset integrity | The embedded archive, runtime helpers, all 32 skybox VPKs, and eight visual fixes are checked with SHA-256. |
| Path confinement | Cache, backup, and addon operations are restricted to validated child paths. |
| Transactional switching | Sources are verified before copying; failed changes roll back to the previous verified file. |
| Unknown-mod preservation | An unfamiliar `pak01_dir.vpk` receives a verified timestamped backup before override. |
| Process guard | Skybox and GameInfo changes are blocked while Deadlock or supported mod managers are running. |
| Cache recovery | Invalid caches are quarantined under an `.invalid-*` name rather than deleted. |
| Config preservation | Applying the saved GameInfo snapshot creates a timestamped backup of the current file. |

The selector does not launch Deadlock and does not remain active after its window is closed.

Asset inspection for the cosmetic switches was powered by [Source 2 Viewer](https://s2v.app) ([ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)).

## Repository Contents

| Path | Purpose |
| --- | --- |
| `source/launcher` | WinForms interface, one-file launcher, manifest, and application icon. |
| `source/gameinfo-installer` | Permission-aware GameInfo component. |
| `source/runtime` | Skybox transactions, optional FPS profile, and compatibility wrapper. |
| `source/config` | Verified GameInfo payload. |
| `unpacked/runtime` | Runtime resources extracted from the release executable. |
| `unpacked/assets` | All previews, manifests, and 32 unpacked VPK files. |
| `unpacked/config` | GameInfo extracted from the embedded component. |
| `tests` | First-run, switching, rollback, backup, startup, and FPS-profile tests. |
| `tools` | Integrity and synthetic test utilities. |

## Credits

Skybox assets are based on **HyperLine's Skybox Replacement v2.0** for Deadlock. Package inspection and VPK validation use **ValveResourceFormat / Source 2 Viewer**. See [CREDITS.md](./CREDITS.md) for attribution.

The skybox assets are third-party mod content. Confirm redistribution permission before publishing mirrors or derivative packages.

---

<div align="center">

Made by **harriton**

</div>
