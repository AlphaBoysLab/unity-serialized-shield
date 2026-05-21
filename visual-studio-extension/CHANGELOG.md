# Changelog

All notable changes to UnitySerializedShield Visual Studio will be documented in this file.

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
