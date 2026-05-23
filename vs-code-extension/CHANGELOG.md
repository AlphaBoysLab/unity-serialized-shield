# Changelog

All notable changes to UnitySerializedShield will be documented in this file.

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
