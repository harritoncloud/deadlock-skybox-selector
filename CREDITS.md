# Credits

- Anime and Realistic v2.0 skybox assets: HyperLine, `Skybox Replacement`
  (GameBanana mod 600744).
- Package inspection and CRC verification: ValveResourceFormat / Source 2 Viewer.
- Floating unit-name override: `Hide Names` (GameBanana mod 722348),
  unmodified `hidename_1ff8f.zip` VPK variant.
- Interface typeface: Titan One by Rodrigo Fuenzalida, SIL Open Font License 1.1.
  Embedded unmodified in the launcher and registered for the running process only,
  so nothing is installed on the machine. Licence text:
  `source/launcher/fonts/TitanOne-OFL.txt`, also embedded in the executable.

This project adds a local selector, integrity checks, backups, and safe switching
for personal mod use. Skybox textures remain unchanged; VPK resource names were
retargeted for the new map. The optional base-veil override reuses Deadlock's
own invisible material and empty particle resource. The pickup-book fix uses
the same stock empty particle in place of the gained-effect model child.
