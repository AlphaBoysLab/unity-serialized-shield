# Changelog

All notable changes to UnitySerializedShield Visual Studio will be documented in this file.

## 1.0.1

- Fixed rename detection so `[FormerlySerializedAs]` is added only for real Visual Studio rename operations such as menu Rename or Ctrl+R.
- Prevented normal typing or single-character edits from creating unwanted `[FormerlySerializedAs]` attributes.

## 1.0.0

- Initial Visual Studio VSIX release.
- Added automatic `[FormerlySerializedAs]` insertion for recognized Unity `[SerializeField]` renames.
- Added automatic `using UnityEngine.Serialization;` insertion when needed.
