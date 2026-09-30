# Deadlock Skybox Selector 1.2.0

Version 1.2 brings skyboxes and visual fixes into one app. The one-file Windows build includes 32 skyboxes with previews, nine optional visual switches, and a saved GameInfo preset.

## What's new

- **Cleaner world:** independently hide the base veil, factory smoke, floating unit names, pickup-book effect, player markers, and distance markers for Graves' zombies and ultimate gravestone. The two marker switches can be combined without changing normal pings.
- **Clearer HUD:** remove health-bar tick marks; restore purple ability-upgrade fills, readable lavender checkmarks, and a purple AP counter. Build-recommended upgrades get a stronger amber highlight. The ability hint, icon, and tooltip cards use matching colors.
- **Softer picture:** ColorFix lowers scene contrast without recoloring the HUD.
- **Simpler controls:** the Fixes page has compact on/off switches. The GameInfo page can reapply its bundled preset at any time and backs up the file it replaces.
- **Updated GameInfo:** the bundled Maxfps-based preset retains the saved low-texture and aspect-ratio settings while leaving `citadel_in_world_item_panel_dpi` at the game's default. In a local in-game test, this removed the blank white square above pickups. The playtester survey remains disabled.
- **Reliability:** the app verifies its embedded assets and refuses to overwrite an unfamiliar visual-fix addon. First-run extraction, cache migration, skybox switching, backups, and marker combinations have automated checks.

## Before installing

Close Deadlock and any mod manager, then run `SkyboxSelector.exe`. Changes to a visual switch appear on the next game launch. The GameInfo preset sets `cl_phys_enabled false`, which may cause game issues. Some saved texture/camera commands have not been confirmed after the game's update, so the preset is **not an FPS guarantee**. See the [README](README.md#gameinfo-preset) for details and the exact config.

SHA-256 of `SkyboxSelector.exe`: `B2E9A59CFFBA6FBBC2254D05B4BEF1479143B29B68422D9E5B9BA5B61505792D`
