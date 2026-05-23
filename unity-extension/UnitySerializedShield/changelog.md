# Changelog

All notable changes to this package will be documented in this file.

## [1.0.2] - 2026-05-23

### Changed

- Removes completed `FormerlySerializedAs` attributes by default after the migration button runs.
- Keeps the direct serialized YAML migration and reserialize step before attribute cleanup.

## [1.0.1] - 2026-05-23

### Changed

- Keeps `FormerlySerializedAs` attributes by default after migration so users can verify Unity data before cleanup.

### Added

- Adds a scoped text migration pass for referenced Unity YAML assets before reserialization.
- Reports how many serialized field keys were migrated in scene, prefab, and asset files.

## [1.0.0] - 2026-05-17

### Added

- Initial release of Unity Serialized Shield.
- Added the Unity Editor migration window for finding and migrating renamed serialized fields.
- Added scanning for scripts that contain `FormerlySerializedAs` attributes.
- Added preview support for referenced scene, prefab, and asset files.
- Added migration backup and latest-backup restore support.
- Added package metadata for Unity Package Manager.
