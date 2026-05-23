# Changelog

All notable changes to UnitySerializedShield will be documented in this file.

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
