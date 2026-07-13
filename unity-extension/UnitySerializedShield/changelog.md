# Changelog

All notable changes to this package will be documented in this file.

## [2.0.0] - 2026-07-13

Safety release addressing the 2026-07-12 audit of the Unity editor package. Major
version bump because migration behavior changes deliberately: migrations that
previously "succeeded" silently in unsafe conditions now abort or refuse cleanup.

### Fixed

- Serialized YAML key renames are now structure-aware: only keys at the top
  indentation level of a component block are renamed, and only in blocks whose own
  `m_Script` entry references the script (fileID 11500000 + GUID). Nested
  `[Serializable]` class members and unrelated components that merely reference the
  script asset are never rewritten (U-C1, U-C2, U-M1).
- Attribute removal is gated on a post-migration verification pass: a
  `FormerlySerializedAs` attribute is removed only when its old name is verified
  absent from every scene, prefab, `.asset`, `.anim`, and `.preset` file, all asset
  kinds were included in the run, and no file failed to read or write. Otherwise
  the attribute is kept and the reason is reported (U-C4, U-C3, U-H9).
- Prefab-instance override `propertyPath` entries and animation/preset bindings
  that still reference an old field name are detected and block attribute removal
  with an explicit warning (U-C3, U-H9).
- Migration refuses to run while Prefab Mode is open, aborts when affected open
  scenes remain dirty after the save prompt ("Don't Save" now aborts instead of
  proceeding), and reloads affected open scenes from disk after files change so a
  later Ctrl+S can no longer revert the migration (U-C5). Backup restore applies
  the same guards (U-M9).
- Backup session folders are uniquely named (timestamp + random suffix) and batch
  migrations share ONE session, so sessions can no longer overwrite each other or
  back up already-migrated content (U-C6).
- The editor assembly definition is restricted to the Editor platform so consumer
  player builds no longer break (U-C7).
- Migration requires Force Text asset serialization and aborts otherwise (U-H1).
- Package assets (`Packages/...`) are scanned via `AssetDatabase.GetAllAssetPaths`
  and resolved through `FileUtil.GetPhysicalPath` where available (U-H2, U-H3).
- Inline `[FormerlySerializedAs("old")] public int x;` declarations are detected;
  comment lines between an attribute and its field no longer break detection;
  combined attribute lists (`[SerializeField, FormerlySerializedAs("old")]`),
  verbatim string arguments, and non-ASCII field names are recognized
  (U-H4, U-H5, U-M2, U-M3).
- Attribute counting, extraction, and removal ignore occurrences inside comments
  and strings; removal of an element inside a combined attribute list keeps the
  other attributes intact (U-M3, U-M4).
- Multi-declarator fields are skipped with an explicit warning instead of mapping
  old names to the wrong declarator (U-M5).
- Serialized files are read and written with their original encoding and BOM
  preserved; undecodable files fail loudly and block cleanup (U-H6).
- Per-file IO failures are collected and reported; rewrites are staged before any
  write; read-only files are reported instead of aborting mid-batch, and version
  control checkout is attempted when a Provider is active (U-H7).
- Backups are created after the scene-save prompt so restoring a backup no longer
  discards just-saved work (U-H8).
- "Migrate All Listed Scripts" isolates per-script failures and reports an
  aggregated summary (U-H10).
- Cancelling a migration no longer reports success; the backup session path is
  shown in the status line (U-M10).
- Physical-to-asset path mapping is case-insensitive and returns failure instead
  of leaking absolute paths into `ForceReserializeAssets` (U-M11).
- Case-insensitive `.cs` matching; field-migration analysis only runs for scripts
  that contain the attribute (U-M8); backup list is cached instead of hitting the
  disk every repaint (U-M7).
- Follow-up re-audit hardening: `.controller` (AnimatorController /
  StateMachineBehaviour) and `.playable` (Timeline) assets are now scanned and
  verified alongside scenes/prefabs/`.asset`, so their script-instance data is
  migrated and can no longer be orphaned by attribute removal (N1). Field-name
  recycling across a single migration pass (A `damage`→`power` while B
  `power`→`attackPower`) can no longer cross-wire values: a key rewritten by one
  migration is invisible to later ones (N2). Prefab-override and animation
  bindings are matched on every `propertyPath` segment, not just the root, so a
  renamed nested field is detected and blocks removal (N3).

### Added

- Dry Run button: per-file, per-line preview of every key rename (`old -> new at
  line N`) without writing anything.
- Cancelable progress bars for scanning, rewriting, and verification.
- One shared backup session per batch migration, per-session Restore buttons, and
  a structured `migration-log.txt` written next to each backup session.
- Old-to-new field mappings and analyzer warnings shown per script in the window.
- Migration options persist across sessions via `EditorPrefs`.
- `Tests/Editor` edit-mode test assembly covering the YAML rewriter and the C#
  script analyzer.

### Changed

- Minimum supported Unity version lowered from 6000.3 to 2019.4.
- Backup entries now store session-relative paths (old absolute-path sessions are
  still restorable).

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
