# Changelog

All notable changes to UnitySerializedShield will be documented in this file.

## 2.0.0

Safety release addressing the 2026-07-12 audit (Part 3). Major version bump
because the default behavior changes: the unreliable passive text-diff
listener is now **off by default** in favor of the LSP-delegated
`Rename Serialized Field` command (F2 / Rename Symbol), and the gating,
offset-mapping and parsing logic is extracted into a pure, fully-tested
module (`renameLogic.ts`).

### Fixed

- Document changes are classified before any action is taken: undo and redo
  are never treated as renames (W-C3), multi-change transactions (including
  multi-cursor typing) and the extension's own programmatic edits are
  skipped (W-C1, W-C12), and single edits are debounced (W-C2). A passive
  rename now only fires when exactly one serialized field changed name and
  the old name no longer appears anywhere in code.
- An old name surviving only in a comment or string no longer blocks a
  rename, and the provider path maps insertion offsets back to pre-edit
  coordinates so the attribute always lands on the correct line even when a
  reference precedes the declaration (W-C4).
- Parser hardening: generic field types containing commas
  (`Dictionary<string, int>`, W-C7); expression-bodied properties and event
  declarations are no longer treated as fields (W-C5); enclosing-type
  resolution is brace-aware so a field after a nested type is attributed to
  the correct type (W-C6); a comment line between an attribute and its field
  no longer breaks association (W-C8); block comments and verbatim strings
  are ignored (W-C9); `#if/#else/#endif` no longer produces phantom
  duplicates (W-C10); `record` / `record struct` are supported (W-C16);
  file-scoped namespaces and `[Serializable]` nested classes are handled;
  BOM and majority line-ending detection are correct (W-R5).
- `applyEdit` failure no longer advances the snapshot baseline, so a failed
  edit is retried instead of permanently lost (W-C11). Change handling and
  timers are exception-guarded (W-R2), and rename-delegation state is no
  longer a naive global flag (W-R3).
- Follow-up re-audit hardening: code inside string-interpolation holes
  (`$"{speed}"`) is preserved by sanitization instead of being blanked as
  string text, so a reference there is no longer missed when checking whether
  an old name is fully gone (N1); attribute lists wrapped across multiple
  lines (`[SerializeField,\n FormerlySerializedAs("x")]`) are now recognized so
  the field stays protected (R3).

### Added

- Configuration surface: `unitySerializedShield.enabled`,
  `enablePassiveRenameDetection` (off by default), `passiveDebounceMs`,
  `enableLogging`, and `showStatusBarNotification`.
- `renameLogic.ts` pure module plus `renameLogic.test.ts` and
  `parser.test.ts` (45 host-free cases); the full suite passes in the VS
  Code test host (63 tests).

### Changed

- The passive text-diff rename listener is disabled by default; the
  LSP-delegated command path is now the primary, recommended flow.

## 1.1.0

- Adds protection for **public** serialized fields that have no explicit `[SerializeField]` attribute. A public instance field inside a `MonoBehaviour`, `ScriptableObject`, `StateMachineBehaviour`, or a `[Serializable]` type is now recognized as Unity-serialized, so renaming it adds `[FormerlySerializedAs]` just like an explicit `[SerializeField]`.
- Skips `readonly` serialized fields (Unity never serializes them), matching the static/const skip rule.
- Skips fields marked `[NonSerialized]`.
- Brings the VS Code parser's serialization rules in line with the Roslyn-based Visual Studio engine.

## 1.0.21

- Upgrades the passive rename detection engine to use a robust, settle-based debounce logic (300ms sweet-spot). This elegantly and permanently replaces the complex `isValidRenameEdit` heuristics. Both multi-file bulk renames (processed instantly) and single-change minimal-diff renames (such as prefix deletions `m_` -> `""` and suffix modifications `5` -> `6` processed after a 300ms settle delay) are now fully supported natively, while incremental typing is perfectly debounced and shielded.

## 1.0.20

- Fixes minimal-diff rename detection issue under C# Dev Kit and OmniSharp. When a serialized field with no other references (where `contentChanges.length === 1`) is renamed from the start (e.g. prefix deletion `m_` -> `""`) or from the end (e.g. replacing a single character/digit `5` -> `6`), the language server optimizes the edit as a minimal-diff text change. We now introduce a mathematically precise `isCompatibleWithRename` checker that matches suffix/prefix modifications, and single-character replacements while keeping incremental typing fully protected.

## 1.0.19

- Implements focus-based active editor snapshotting and visible editor pre-population to guarantee baseline snapshots are always fresh.
- Introduces a robust disk-fallback baseline recovery mechanism inside the passive change listener. If a background document is loaded on the fly during a solution-wide rename and lacks an in-memory snapshot, we automatically read the pre-rename baseline from the clean file on disk (since the unsaved editor buffer contains the renamed text).
- Cleans up internal diagnostic logging for the production release.

## 1.0.17

- Fixes critical VS Code baseline loss issue caused by background tab garbage collection. When solution-wide F2 renames are executed, background documents are loaded on the fly, triggering open events. We now preserve baseline snapshots across background close events and only overwrite them if documents are not dirty, preventing baseline corruption.
- Implements a robust bulk edit failsafe so that any verified identifier replacement is treated as a valid rename command.

## 1.0.16

- Fixes VS Code rename-detection failure under C# Dev Kit/OmniSharp where minor formatting changes (like whitespace/semicolons) inside the rename's text change transaction would cause the extension to ignore the rename. The extension now safely ignores formatting edits and correctly inserts the `[FormerlySerializedAs]` attribute.

## 1.0.15

- Fixes the baseline shifting rename detection bug where incremental keystrokes or intermediate reference changes committed by C# Dev Kit/OmniSharp during F2 inline rename would prematurely update the snapshot baseline.
- Implements a robust pending queue, debounce delay, and original baseline preservation system mirroring the Visual Studio extension.

## 1.0.14

- Adds a robust passive `onDidChangeTextDocument` listener fallback to support all VS Code rename providers (including Microsoft's C# Dev Kit and OmniSharp).
- Even if C# Dev Kit's provider has higher priority and completely bypasses our custom `RenameProvider`, our passive listener will detect the completed rename transaction in the editor buffer and cleanly apply `[FormerlySerializedAs]` programmatically.
- Integrates whole-identifier identifier-diffing checks (`isRenameCommandEdit`) to ensure typing character-by-character never triggers false-positive attributes.

## 1.0.13

- Fixes a critical rename-detection bug where MonoBehaviours containing multiple serialized fields of the same type (e.g. multiple `private string` fields) were completely ignored.
- Aligns the VS Code duplicate keys matching logic with the robust index-based grouping used in Visual Studio, ensuring all renames work cleanly.

## 1.0.12

- Added a VS Code Rename Provider so native F2 and built-in Rename Symbol can keep the inline rename experience.
- The provider delegates to the C# rename provider and adds `[FormerlySerializedAs]` to the same rename edit when a serialized field is renamed.

## 1.0.11

- Added `UnitySerializedShield: Rename Serialized Field` to the VS Code editor right-click menu for C# files.
- Kept F2 bound to the same protected rename command.

## 1.0.10

- Replaced passive VS Code rename detection with a deterministic F2 command.
- `UnitySerializedShield: Rename Serialized Field` now prompts for the new name, runs VS Code's rename provider, then applies `[FormerlySerializedAs]`.
- Normal typing no longer triggers migration attributes.

## 1.0.9

- Changed VS Code F2 handling to run through `UnitySerializedShield: Rename Serialized Field`.
- Stopped passive typing detection in VS Code so normal variable edits do not add `[FormerlySerializedAs]`.
- The F2 command still opens VS Code's built-in Rename Symbol UI, then applies `[FormerlySerializedAs]` after the completed rename.

## 1.0.8

- Fixed VS Code Rename Symbol when the rename UI emits single-character live edits.
- Debounced single-character edit bursts and applies `[FormerlySerializedAs]` only after the final settled rename is detected.

## 1.0.7

- Stopped single-character typing edits from adding `[FormerlySerializedAs]` repeatedly.
- Kept VS Code F2 Rename Symbol support working for real rename edits.

## 1.0.6

- Restored the reliable previous/current text rename detection path so VS Code F2 Rename Symbol adds `[FormerlySerializedAs]` again.

## 1.0.5

- Fixed VS Code F2 Rename Symbol support when the C# rename provider reports partial identifier edits.
- Added a short settled-document check so rename batches are detected reliably before inserting `[FormerlySerializedAs]`.

## 1.0.4

- Fixed rename detection so real editor rename operations add `[FormerlySerializedAs]` again.
- Kept protection against accidental single-character typing edits creating migration attributes.

## 1.0.3

- Fixed rename detection so `[FormerlySerializedAs]` is added only for real editor rename operations such as F2/menu Rename Symbol.
- Prevented normal typing or single-character edits from creating unwanted `[FormerlySerializedAs]` attributes.

## 1.0.2

- Updated the Marketplace display name to `UnitySerializedShield`.

## 1.0.1

- Added the extension icon for VS Code and Marketplace display.

## 1.0.0

- Initial Marketplace release.
- Added automatic `[FormerlySerializedAs]` insertion for recognized Unity `[SerializeField]` renames.
- Added automatic `using UnityEngine.Serialization;` insertion when needed.
- Added `UnitySerializedShield: Show Status` command.
