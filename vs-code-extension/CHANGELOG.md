# Changelog

All notable changes to UnitySerializedShield will be documented in this file.

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
