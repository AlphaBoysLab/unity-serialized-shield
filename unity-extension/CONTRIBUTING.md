# Contributing to the Unity Extension

This folder contains the Unity package version of UnitySerializedShield.

The Unity extension provides Editor tooling for scanning serialized field migrations and helping protect Unity project data during field renames.

## Package Location

The Unity package lives here:

```text
unity-extension/UnitySerializedShield
```

Important files:

- `package.json`: Unity package metadata
- `Editor/SerializedShieldMigrationWindow.cs`: Editor window UI
- `Editor/SerializedShieldMigrationScanner.cs`: scan logic
- `Editor/SerializedShieldMigrationProcessor.cs`: migration processing
- `Editor/SerializedShieldMigrationTypes.cs`: shared migration models
- `Editor/SerializedShieldPathUtility.cs`: path helpers
- `Editor/SerializedShieldMigrationBackup.cs`: backup helpers

## Unity Setup

To test changes, use a Unity project and add this package from disk:

```text
unity-extension/UnitySerializedShield
```

In Unity Package Manager:

1. Open `Window > Package Manager`.
2. Click `+`.
3. Select `Add package from disk...`.
4. Choose `unity-extension/UnitySerializedShield/package.json`.

## Manual Testing

Before opening a pull request, test the Unity package in the Unity Editor:

1. Open a Unity project with serialized MonoBehaviour fields.
2. Create or use a script with `[SerializeField]` fields.
3. Rename a field and add `[FormerlySerializedAs]`.
4. Open the UnitySerializedShield Editor window.
5. Run the scan/migration workflow.
6. Confirm scene, prefab, and asset data are handled as expected.

Use test assets that can be safely modified. Do not run migration tests on important project data without a backup.

## Code Guidelines

- Keep Editor-only code under the `Editor/` folder.
- Do not add runtime dependencies unless they are necessary.
- Keep Unity API usage compatible with the package `unity` version in `package.json`.
- Prefer clear migration reports and safe failure behavior.
- Preserve or improve backup behavior for operations that modify project files.
- Do not commit Unity generated folders such as `Library/`, `Temp/`, `Obj/`, `Logs/`, or `UserSettings/`.

## Package Metadata

When changing user-facing behavior, update the relevant package docs:

- `UnitySerializedShield/readme.md`
- `UnitySerializedShield/changelog.md`
- `UnitySerializedShield/package.json` version when preparing a release

Keep `.meta` files with package assets when Unity creates or updates them.

## Pull Request Checklist

- Tested in Unity Editor.
- Documented manual test steps.
- Updated package docs/changelog if behavior changed.
- No generated Unity folders included.
- No unrelated formatting churn.
