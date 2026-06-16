# Changelog

All notable changes to UnitySerializedShield Visual Studio will be documented in this file.

## 2.0.0

- Major rewrite onto a Roslyn-powered, in-process engine for accurate, solution-wide rename detection.
- Renaming a serialized field with Rename Symbol (Ctrl+R, R) now adds `[FormerlySerializedAs("oldName")]` to the field **declaration** even when the rename is started from a reference in another file or class — true Rider-like, cross-file behavior.
- Detection is semantic (Roslyn `VisualStudioWorkspace`) instead of text/regex based, and only reacts to the actual Rename command, never to character-by-character typing.
- Conservative skips preserved: static/const/readonly fields, `[NonSerialized]`, multi-field declarations, and non-Unity types.
- The shipped package no longer bundles the vulnerable transitive MessagePack assembly.

## 1.0.54

- Maintenance release. Rebuilt and repackaged the extension against the current Visual Studio toolchain; no functional changes to serialized-field rename detection. A separate Roslyn-based, solution-wide rename engine is in development for a future major release.

## 1.0.53

- Restores automatic document saving after applying programmatic edits (attribute insertions and cleanup removals). Because Visual Studio's solution rename engine automatically saves variable renames to disk, keeping the added migration attribute unsaved allowed Unity to compile the rename-only script first, resulting in reference loss in the Unity Inspector. Automatically saving ensures Unity always recompiles with the attribute, fully preserving Inspector references.

## 1.0.52

- Removes automatic file-saving (SaveDocumentAsync calls) from both `ApplyEditsAsync` and `VerifySavedMigrationAsync` in the Visual Studio listener.
- Keeps modified C# files in an unsaved/dirty state inside the Visual Studio editor after inserting the `[FormerlySerializedAs]` attribute. This aligns the experience with VS Code, gives the user full undo/redo control, and ensures that the rename and the migration attribute are saved to disk simultaneously in a single user save transaction.

## 1.0.51

- Resolves a critical C# script data-loss bug in Unity. Because the extension previously waited 500ms before applying the `[FormerlySerializedAs]` attribute, Visual Studio committed and saved the rename-only script first. Unity would immediately recompile the rename-only file and destroy the field's serialized variable data in the Inspector before the attribute could be added.
- Reduced the rename command apply delay to 50ms and the prefix rename delay to 100ms. Supported by the `LastAppliedEditTimes` event cool-down cache, these fast apply times are safe and ensure that Unity receives the attribute simultaneously with the rename, successfully migrating and preserving all serialized variable data.

## 1.0.50

- Resolves a critical race condition where Visual Studio's `Rename Symbol` engine would trigger a spurious reverse-rename event (e.g. `m_PlayerLevel223 -> m_PlayerLevel1`) immediately after our programmatic attribute insertion. Implemented a thread-safe `LastAppliedEditTimes` timestamp cache to discard any editor events within a 1000ms cool-down window of our edits, while updating current snapshots.
- Fully integrates self-referential attribute cleanups (`BuildSelfAttributeRemovals`) in both the rename commit (`ApplyPendingRenameAsync`) and settle (`VerifySavedMigrationAsync`) phases of the Visual Studio listener, ensuring that renaming a field back to its old name cleans up any redundant attributes.
- Increases the Rename Symbol apply settle delay to 500ms to allow Visual Studio's editor engine to stabilize.

## 1.0.49

- Fixes a critical Visual Studio edit-overlap issue where self-referential attribute cleanups (e.g. `[FormerlySerializedAs("m_PlayerLevel1")]` on a field named `m_PlayerLevel1`) were ignored or failed when applied in the same transaction as a new attribute insertion.
- Adjusts insertion offsets dynamically in `ApplyEditsAsync` when they coincide with a removal offset, ensuring both actions succeed perfectly.

## 1.0.48

- Removes the restriction blocking incremental (character-by-character) serialized field renames on fields that already have `[FormerlySerializedAs]` attributes.
- Allows chaining multiple `[FormerlySerializedAs]` attributes naturally for sequential renames, matching the professional refactoring experience of Rider IDE.

## 1.0.47

- Fixes pending rename operations being cancelled when the user renames a second field before the first rename has been applied.
- Computes event-level renames from the actual event before-text instead of the cached snapshot, so classification functions correctly see only the current event's changes.
- Carries forward a confirmed rename flag from pending operations so subsequent edits in the same file do not discard an already-confirmed rename.

## 1.0.46

- Fixes baseline shifting during incremental and consecutive field renames, ensuring original field names are correctly preserved.
- Prevents invalid self-referential attribute insertions (like `[FormerlySerializedAs("m_EnemyName1")]` above `m_EnemyName1`) and ensures `[FormerlySerializedAs]` attributes on other fields are not lost during multi-field renames.
- Utilizes precise event-level diffing (`currentEventRenames`) for accurate small-rename classification.

## 1.0.45

- Expands the guarded small `Ctrl+R, Ctrl+R` rename fallback to cover two-character Unity prefix changes such as `m_PlayerName` to `PlayerName`.
- Keeps suffix and underscore small rename support for cases such as `m_EnemyName` to `m_EnemyName1`, while still blocking repeated chains on fields that already have migration attributes.

## 1.0.44

- Restores support for `Ctrl+R, Ctrl+R` one-character serialized field renames such as `m_PlayerName` to `m_PlayerName1`, `m_PlayerLevel` to `m_PlayerLevel_`, and `m_EnemyName` to `m_EnemyName1`.
- Blocks this small-rename fallback on fields that already have `[FormerlySerializedAs]`, preventing repeated attribute chains while editing migrated fields.

## 1.0.43

- Stops treating ordinary single-character typing inside serialized field names as a rename command.
- Requires a whole-identifier or bulk Rename Symbol style edit before adding `[FormerlySerializedAs]`, preventing long attribute chains while the user types.

## 1.0.42

- Removes self-referential `[FormerlySerializedAs]` attributes such as `[FormerlySerializedAs("PlayerLevel1")]` above `PlayerLevel1`.
- Allows `_PlayerLevel1` to `PlayerLevel1` and similar prefix-removal renames to add the real previous serialized name while cleaning stale self attributes.

## 1.0.41

- Uses Visual Studio's immediate before-text as the baseline for new renames after Unity migration cleanup, fixing cases such as `EdnemyName3` to `m_EnemyName`.
- Keeps cached snapshots only while a rename operation is actively pending.

## 1.0.40

- Refreshes stale document baselines after Unity migration cleanup so `m_PlayerName1` to `_PlayerName1` records `m_PlayerName1`, not a self-reference.

## 1.0.39

- Adds a settled single-character rename candidate path for Rename Symbol edits such as `EnemyName2` to `EdnemyName2`.

## 1.0.38

- Detects affix insertion/deletion rename shapes such as `sEnemyName1` to `_sEnemyName1`.

## 1.0.37

- Advances the rename baseline after inserting migration attributes so chained renames use the latest field name, such as `sEnemyName1` to `_sEnemyName1`.

## 1.0.36

- Broadens Rename Symbol rename coverage for prefix, suffix, middle, casing, underscore, and mixed Unity field rename styles.

## 1.0.35

- Detects prefix-fragment Rename Symbol changes such as `m_EnemyName` to `sEnemyName`.

## 1.0.34

- Detects plain trailing-number changes such as `ShahriarPlayerName1` to `ShahriarPlayerName2`.

## 1.0.33

- Prevents invalid self-referential migration attributes such as `[FormerlySerializedAs("m_EnemyName_th")]` above `m_EnemyName_th`.

## 1.0.32

- Detects plain numeric suffix Rename Symbol changes such as `ShahriarPlayerName` to `ShahriarPlayerName1`.

## 1.0.31

- Detects Rename Symbol changes from any serialized field to a very different valid field name, such as `playerName` to `ShahriarPlayerName`.

## 1.0.30

- Detects Rename Symbol changes from Unity `m_` private fields to arbitrary valid field names such as `m_PlayerName` to `_ShahriarplayerName`.
- Stops auto-saving files after inserting `[FormerlySerializedAs]`; the document remains dirty so users can undo or save normally.

## 1.0.29

- Detects Unity private field prefix cleanup renames such as `m_PlayerName` to `PlayerName`.
- Keeps .NET 10 VSIX metadata so the packaged extension declares `net10.0` support.

## 1.0.28

- Improves numeric suffix rename detection for final field names such as `playerName` to `playerName_2` and `m_playerLevel` to `m_playerLevel_1`.
- Adds test coverage for Unity-style private fields with `m_` prefixes.
- Retargets the Visual Studio extension projects to .NET 10.

## 1.0.27

- Detects Rename Symbol numeric suffix changes such as `enemyName_1` to `enemyName_3` and `velocity_1` to `velocity_2`.
- Keeps support for numeric suffix additions such as `enemyName` to `enemyName_1`.

## 1.0.26

- Detects Rename Symbol suffix edits such as `enemyName` to `enemyName_1` and `velocity` to `velocity_1`.
- Keeps the suffix path narrow to numeric suffix additions so ordinary arbitrary typing is not treated as a migration rename.

## 1.0.25

- Debounces Rename Symbol live-edit updates so intermediate names such as `opponentName_` are not written as `[FormerlySerializedAs]` history.
- Keeps the original pre-rename baseline while waiting for the final inline rename text.

## 1.0.24

- Removes the remaining delay before applying `[FormerlySerializedAs]` for recognized Rename Symbol edits.
- Reduces the chance that Unity or Visual Studio auto-save can import a rename-only script before the migration attribute is written.

## 1.0.23

- Applies and saves `[FormerlySerializedAs]` almost immediately for recognized Rename Symbol edits, reducing the window where Unity could import a rename-only script.
- Keeps the delayed verification repair pass for cases where Visual Studio overwrites the first insertion after rename settles.

## 1.0.22

- Adds `[FormerlySerializedAs]` only for Visual Studio Rename Symbol whole-identifier replacement edits.
- Ignores serialized field name edits that do not come through the recognized menu/Ctrl+R rename edit path.

## 1.0.21

- Prevents normal single-character typing in serialized field names from adding repeated `[FormerlySerializedAs]` attributes.
- Keeps Rename Symbol support by accepting whole serialized identifier replacement edits and settled multi-event rename candidates.

## 1.0.20

- Accepts any settled serialized field rename candidate instead of rejecting non-small one-shot rename edits.
- Fixes Visual Studio Rename Symbol cases such as `m_FileName` to `fileName`.

## 1.0.19

- Accepts small serialized field renames such as `StringValue_1` to `StringValue1`.
- Saves the C# document immediately after inserting `[FormerlySerializedAs]` so Unity imports the migration attribute instead of a rename-only script.
- Adds a post-insert verification pass that restores missing `[FormerlySerializedAs]` attributes if Visual Studio's rename operation overwrites the first insertion.
- Adds coverage for renaming a serialized field after other fields already have `[FormerlySerializedAs]` attributes.

## 1.0.18

- Fixed rename detection for fields that appear only once in a file. Ctrl+R / Rename Symbol now adds `[FormerlySerializedAs]` reliably even when the field has a single reference.
- Detects whole-identifier replacement (Rename Symbol) vs. single-character typing to avoid false positives without requiring duplicate rename events.
- Preserves the original field name baseline while a rename operation is pending. Prevents losing the true old name when multiple renames happen in sequence.
- Adds a post-insert verification pass that restores missing `[FormerlySerializedAs]` attributes if Visual Studio's rename operation overwrites the first insertion.
- Saves the C# document immediately after inserting `[FormerlySerializedAs]` so Unity imports the migration attribute instead of a rename-only script.
- Fixed `StripLineComment` to handle C# verbatim strings (`@"..."`) so that `//` inside verbatim string initializers is not incorrectly treated as a comment.
- Fixed serialized field parsing for generic types with commas (e.g., `Dictionary<string, int>`). Commas inside angle brackets no longer cause the field to be skipped.
- Made diagnostic fields thread-safe with `volatile`.
- Increased rename settle delay from 350ms to 400ms for more reliable debouncing.

## 1.0.17

- Uses a repeated rename-event confirmation before inserting `[FormerlySerializedAs]`.
- Prevents one-off field-name typing edits from creating migration attributes while keeping Visual Studio Rename Symbol support.
- Keeps manual-save behavior after migration attributes are inserted.

## 1.0.16

- Ignores normal character-by-character typing inside serialized field names.
- Only applies `[FormerlySerializedAs]` when Visual Studio reports a whole-identifier replacement for the serialized field rename.

## 1.0.15

- Stops auto-saving C# files after inserting `[FormerlySerializedAs]`.
- Leaves the migration attribute in the Visual Studio editor buffer so users can review and save manually.
- Removes the delayed saved-file repair pass to avoid writing files without user action.

## 1.0.14

- Detects serialized field renames when another field has the same type and no initializer.
- Matches fields by order within the same serialized field shape instead of skipping common Unity declarations as ambiguous.

## 1.0.13

- Adds a delayed saved-file verification pass after Visual Studio rename edits.
- Restores missing `[FormerlySerializedAs]` attributes on disk if Visual Studio overwrites the editor insertion after the rename completes.
- Reduces the rename settle delay so migrations appear faster after Rename Symbol.

## 1.0.12

- Fixed a Visual Studio edit/save failure that could happen after inserting `[FormerlySerializedAs]`.
- Saves after the migration edit through Visual Studio's document API instead of saving inside the edit batch.
- Removed the retry path that could double-apply migration edits and leave the editor in an unstable state.

## 1.0.11

- Saves the C# document immediately after inserting `[FormerlySerializedAs]`.
- Reduces the risk of Unity importing a rename-only script before the migration attribute is written to disk.

## 1.0.10

- Accepts every confirmed serialized-field rename after debounce instead of rejecting Visual Studio single-edit rename events.
- Adds a post-apply verification pass and retries once if Visual Studio overwrites the inserted migration attribute.
- Improves repeated rename chains so each new field name can receive its own `[FormerlySerializedAs]` entry.

## 1.0.9

- Updated the status command to show the real loaded extension version, listener diagnostics, and log path.
- Helps distinguish the installed local VSIX version from stale Visual Studio Marketplace detail metadata.

## 1.0.8

- Replaced overlapping delayed rename handlers with a per-document debounce so only the latest Visual Studio rename operation applies.
- Preserves the original serialized field baseline while Visual Studio emits multiple rename edit events.
- Reduced the settle delay to make insertion feel faster after Rename Symbol.

## 1.0.7

- Waits briefly for Visual Studio rename operations to settle before inserting `[FormerlySerializedAs]`.
- Recomputes migration insertions against the final editor buffer so Visual Studio does not overwrite the added attribute during rename completion.

## 1.0.6

- Fixed the local amd64 VSIX packaging so Visual Studio installs `.vsextension/extension.json` and other contribution assets correctly.
- Ensures Visual Studio can load the command and editor listener after local installation.

## 1.0.5

- Added local diagnostics at `%LOCALAPPDATA%\UnitySerializedShield\VisualStudioExtension.log`.
- Made Visual Studio rename detection less brittle while still ignoring likely single-character typing edits.
- Improved handling for Visual Studio rename events whose edit shape differs from VS Code rename edits.

## 1.0.4

- Fixed Visual Studio rename detection to use the extension's stored document snapshot as the old text baseline.
- Improved support for Visual Studio rename events that arrive as several edits or with an unreliable before snapshot.
- Added clearer status diagnostics for ignored editor changes.

## 1.0.3

- Preserved the original serialized field name during multi-step Visual Studio rename edits.
- Fixed a Unity migration risk where `[FormerlySerializedAs]` could be generated with a partial intermediate name instead of the true old field name.
- Kept normal single-character typing edits from creating migration attributes.

## 1.0.2

- Fixed rename detection so real Visual Studio rename operations add `[FormerlySerializedAs]` again.
- Kept protection against accidental single-character typing edits creating migration attributes.

## 1.0.1

- Fixed rename detection so `[FormerlySerializedAs]` is added only for real Visual Studio rename operations such as menu Rename or Ctrl+R.
- Prevented normal typing or single-character edits from creating unwanted `[FormerlySerializedAs]` attributes.

## 1.0.0

- Initial Visual Studio VSIX release.
- Added automatic `[FormerlySerializedAs]` insertion for recognized Unity `[SerializeField]` renames.
- Added automatic `using UnityEngine.Serialization;` insertion when needed.
