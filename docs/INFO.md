# UnitySerializedShield Repository Information

UnitySerializedShield is a multi-part tooling project for protecting Unity serialized data when C# fields are renamed. The repository is organized so each editor/tool integration can live in its own folder while sharing one project identity and one license.

The main idea is simple:

1. A developer renames a Unity serialized C# field.
2. The code editor integration adds Unity's `[FormerlySerializedAs("oldName")]` attribute when the rename can be detected safely.
3. The Unity Editor package scans for those migration attributes.
4. Unity reserializes affected scenes, prefabs, and assets so old serialized values are moved to the new field names.
5. After the migration is complete, the Unity package can remove completed `[FormerlySerializedAs]` attributes.

This protects Inspector-assigned values from being lost during refactors.

## Repository Layout

```text
unity-serialized-shield/
|-- docs/
|   `-- INFO.md
|-- unity-extension/
|   `-- UnitySerializedShield/
|       |-- Editor/
|       |-- package.json
|       |-- readme.md
|       |-- changelog.md
|       `-- license.md
|-- vs-code-extension/
|   |-- src/
|   |-- images/
|   |-- package.json
|   |-- package-lock.json
|   |-- README.md
|   |-- DEVELOPMENT.md
|   |-- CONTRIBUTING.md
|   |-- PUBLISHING.md
|   `-- SUPPORT.md
|-- visual-studio-extension/
|   `-- README.md
|-- README.md
|-- LICENSE
|-- .gitignore
|-- .gitattributes
`-- .vscodeignore
```

## Root Files

### `README.md`

The root README gives the short project summary:

```text
Safely rename Unity serialized fields with VS Code and VS tooling and a Unity Editor migration window.
```

This root README should stay small and point users toward the correct subproject documentation.

### `LICENSE`

The repository uses the MIT license.

### `.gitignore`

The root ignore file is configured for a mixed Unity, Visual Studio Code, Visual Studio, and Node.js repository.

It ignores generated content such as:

- `node_modules/`
- `out/`
- `dist/`
- `.vscode-test/`
- `*.vsix`
- Unity `Library/`, `Temp/`, `Obj/`, `Build/`, `Logs/`, `UserSettings/`
- Visual Studio `.vs/`, `bin/`, `obj/`, `TestResults/`
- generated `.sln`, `.csproj`, `.user`, `.pdb`, and similar IDE files

Important Unity package files are not ignored. Files such as `.cs`, `.asmdef`, `.meta`, `package.json`, `readme.md`, and `changelog.md` should be tracked.

### `.vscodeignore`

This file is used when packaging a VS Code extension. It controls which files are excluded from the generated `.vsix` package.

## Unity Package

Path:

```text
unity-extension/UnitySerializedShield/
```

This folder contains the Unity Package Manager package.

Package manifest:

```json
{
  "name": "com.alphaboyslab.unity-serialized-shield",
  "version": "1.0.0",
  "displayName": "Unity Serialized Shield",
  "unity": "6000.3",
  "license": "MIT"
}
```

### Purpose

The Unity package handles the Unity-side migration. It does not detect editor text changes. Instead, it looks for scripts that already contain `[FormerlySerializedAs]` attributes and then helps migrate serialized Unity files.

The package can:

- Scan scripts for `[FormerlySerializedAs]`.
- Show which old serialized names were found.
- Preview referenced serialized files.
- Reserialize scenes, prefabs, and `.asset` files that reference the script.
- Create backup sessions before migration.
- Restore the latest backup session.
- Remove completed `[FormerlySerializedAs]` attributes after migration.

### Unity Menu

The migration window is opened from:

```text
Tools > SerializedShield > Migration Window
```

### Main Unity Editor Files

```text
unity-extension/UnitySerializedShield/Editor/
```

Important files:

- `SerializedShieldMigrationWindow.cs`
- `SerializedShieldMigrationScanner.cs`
- `SerializedShieldMigrationProcessor.cs`
- `SerializedShieldMigrationBackup.cs`
- `SerializedShieldMigrationTypes.cs`
- `SerializedShieldPathUtility.cs`
- `AlphaBoysLab.SerializedShield.Editor.asmdef`

### `SerializedShieldMigrationWindow.cs`

This is the Unity Editor UI. It exposes the migration workflow through an `EditorWindow`.

The window provides:

- Scan button.
- Include prefabs toggle.
- Include scenes toggle.
- Include asset files toggle.
- Create backup toggle.
- Remove attributes after migration toggle.
- Preview references action.
- Migrate one script action.
- Migrate all visible scripts action.
- Restore latest backup action.

### `SerializedShieldMigrationScanner.cs`

This file finds scripts that contain `[FormerlySerializedAs]` attributes.

It uses Unity's asset database to find `MonoScript` assets, reads script text, and extracts migration attributes. It also finds serialized assets that reference a script GUID.

It supports scanning serialized file types controlled by migration options:

- Prefabs
- Scenes
- Asset files

It also contains cleanup helpers for counting and removing `[FormerlySerializedAs]` attributes.

### `SerializedShieldMigrationProcessor.cs`

This file performs the migration work.

Its responsibilities include:

- Finding target serialized files for a script.
- Creating a backup session when enabled.
- Calling `AssetDatabase.ForceReserializeAssets(...)`.
- Removing `[FormerlySerializedAs]` attributes when cleanup is enabled.
- Saving and refreshing the Unity asset database.

### `SerializedShieldMigrationBackup.cs`

This file creates and restores migration backup sessions.

Backups are stored in a project-level folder named:

```text
SerializedShieldMigrationBackups
```

Each backup session includes a `session.json` file and copied versions of the files that may be changed by migration.

### `SerializedShieldMigrationTypes.cs`

This file contains serializable data classes used by the migration system.

Important types:

- `SerializedShieldScriptInfo`
- `SerializedShieldMigrationOptions`
- `SerializedShieldMigrationResult`
- `SerializedShieldBackupSession`
- `SerializedShieldBackupEntry`

Default migration options include:

- Include prefabs: enabled
- Include scenes: enabled
- Include asset files: enabled
- Remove attributes after migration: enabled
- Create backup: enabled

### `SerializedShieldPathUtility.cs`

This file contains path helpers for project-relative and absolute file handling.

### Unity Package Installation

During local development:

1. Open Unity.
2. Open `Window > Package Manager`.
3. Click the `+` button.
4. Select `Add package from disk...`.
5. Choose:

```text
unity-extension/UnitySerializedShield/package.json
```

For Git installation, the package path should point to the package folder:

```text
https://github.com/<user>/<repo>.git?path=/unity-extension/UnitySerializedShield
```

## VS Code Extension

Path:

```text
vs-code-extension/
```

This folder contains the Visual Studio Code extension.

Extension manifest:

```json
{
  "name": "unity-serialized-shield",
  "displayName": "UnitySerializedShield",
  "version": "1.0.2",
  "publisher": "alphaboyslab",
  "engines": {
    "vscode": "^1.118.0"
  }
}
```

### Purpose

The VS Code extension protects serialized fields at the source-code level.

When a C# file changes, it compares the previous document text with the new document text. If it sees that a Unity `[SerializeField]` field kept the same shape but changed name, it inserts:

```csharp
[FormerlySerializedAs("oldName")]
```

If the script does not already import Unity's serialization namespace, it also inserts:

```csharp
using UnityEngine.Serialization;
```

### Example

Before rename:

```csharp
using UnityEngine;

public class EnemySensor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 100f;
}
```

After renaming `maxDistance` to `attackDistance`, the extension updates the script:

```csharp
using UnityEngine;
using UnityEngine.Serialization;

public class EnemySensor : MonoBehaviour
{
    [FormerlySerializedAs("maxDistance")]
    [SerializeField] private float attackDistance = 100f;
}
```

### Main VS Code Source Files

```text
vs-code-extension/src/
```

Important files:

- `extension.ts`
- `formerlySerializedAs.ts`
- `serializedFieldParser.ts`
- `textUtils.ts`
- `test/extension.test.ts`

### `extension.ts`

This is the VS Code extension entry point.

It:

- Activates for C# files and workspaces containing C# scripts.
- Stores snapshots of opened C# documents.
- Watches document change events.
- Compares old and new text.
- Applies generated text edits.
- Registers the command `UnitySerializedShield: Show Status`.

The status command shows:

```text
UnitySerializedShield is watching Unity serialized field renames.
```

### `formerlySerializedAs.ts`

This file contains the rename detection and edit creation logic.

It:

- Parses serialized fields from the previous text.
- Parses serialized fields from the current text.
- Matches fields by declaration shape.
- Detects name changes.
- Avoids duplicate `[FormerlySerializedAs]` attributes.
- Adds the `UnityEngine.Serialization` using statement when needed.

### `serializedFieldParser.ts`

This file parses common Unity serialized field declarations.

Supported examples:

```csharp
[SerializeField] private float maxDistance = 100f;
```

```csharp
[SerializeField]
private float maxDistance = 100f;
```

```csharp
[Header("Movement")]
[SerializeField] private float maxDistance = 100f;
```

Skipped examples:

```csharp
[SerializeField] private int a, b;
```

```csharp
[SerializeField] private static int value;
```

```csharp
[SerializeField] private const int value = 1;
```

Skipping ambiguous cases is intentional. The extension should avoid guessing when a rename is not clear.

### `textUtils.ts`

This file contains shared text helpers for:

- Splitting lines.
- Detecting line endings.
- Stripping line comments.
- Normalizing whitespace.
- Escaping C# strings.
- Escaping regular expressions.

### VS Code Extension Commands

Install dependencies:

```powershell
cd vs-code-extension
npm install
```

Compile:

```powershell
npm run compile
```

Watch during development:

```powershell
npm run watch
```

Run lint and tests:

```powershell
npm test
```

Package a VSIX:

```powershell
npm run package:vsix
```

Publish with `vsce`:

```powershell
npm run publish:vsce
```

### Generated VS Code Files

These files and folders are generated and should not be committed:

- `vs-code-extension/node_modules/`
- `vs-code-extension/out/`
- `vs-code-extension/.vscode-test/`
- `vs-code-extension/*.vsix`

They are ignored by Git.

## Visual Studio Extension

Path:

```text
visual-studio-extension/
```

This folder is reserved for a future Visual Studio extension.

Current status:

```text
Under construction
```

The planned purpose is similar to the VS Code extension: protect Unity serialized fields during C# refactoring from inside Visual Studio.

When implementation starts, this folder may contain:

- Visual Studio extension project files.
- A VSIX manifest.
- C# source code for extension commands and event handling.
- Documentation for installing and testing the Visual Studio extension.

## Complete Intended Workflow

### 1. Rename In The Code Editor

In VS Code, the developer renames a serialized Unity field:

```csharp
[SerializeField] private float maxDistance = 100f;
```

to:

```csharp
[SerializeField] private float attackDistance = 100f;
```

The VS Code extension inserts:

```csharp
[FormerlySerializedAs("maxDistance")]
```

### 2. Open Unity

Unity sees the new field and the migration attribute.

This tells Unity that serialized data previously stored as `maxDistance` should now map to `attackDistance`.

### 3. Run Unity Migration Window

Open:

```text
Tools > SerializedShield > Migration Window
```

Then:

1. Click `Scan`.
2. Review scripts that contain `[FormerlySerializedAs]`.
3. Use `Preview References`.
4. Keep backup enabled.
5. Run migration for one script or all listed scripts.
6. Review changed scenes, prefabs, and assets in version control.
7. Commit the migration result.

### 4. Cleanup Attributes

If `Remove FormerlySerializedAs attributes after migration` is enabled, the Unity package removes migration attributes after successful reserialization.

This keeps scripts clean after serialized data has been updated.

## Safety Notes

Unity serialized migration can touch many files. A single field rename may update:

- `.unity` scene files
- `.prefab` files
- `.asset` files

Before running migration:

- Commit current work or create a backup branch.
- Keep the migration backup option enabled.
- Preview references before migrating.
- Review Git changes after migration.
- Test affected scenes and prefabs in Unity.

The VS Code extension is intentionally conservative. It skips ambiguous code instead of adding a possibly wrong migration attribute.

## Development Guidelines

### Unity Package Changes

When changing the Unity package:

- Keep code inside the `Editor/` folder for editor-only behavior.
- Keep runtime assemblies out of the package unless runtime support becomes necessary.
- Preserve `.meta` files.
- Avoid committing Unity generated folders.
- Test the package through Unity Package Manager using `Add package from disk...`.

Recommended manual test:

1. Create a Unity test project.
2. Install the local package.
3. Create a MonoBehaviour with a `[SerializeField]` field.
4. Assign a value in the Inspector.
5. Rename the field and add `[FormerlySerializedAs]`.
6. Open the migration window.
7. Scan, preview, migrate, and confirm the value remains.

### VS Code Extension Changes

When changing the VS Code extension:

- Add tests for parser or rename behavior changes.
- Run `npm test`.
- Test manually in the Extension Development Host.
- Keep generated `out/` and `.vsix` files out of Git.

Recommended manual test:

1. Open `vs-code-extension/` in VS Code.
2. Press `F5`.
3. In the Extension Development Host, open a Unity C# file.
4. Rename a `[SerializeField]` field.
5. Confirm the attribute and using statement are inserted correctly.

### Visual Studio Extension Changes

The Visual Studio extension is not implemented yet. When development starts, keep the same safety rules:

- Detect only safe serialized field renames.
- Avoid guessing in ambiguous code.
- Do not modify Unity assets from Visual Studio.
- Let the Unity package handle serialized asset migration.

## Release Responsibilities

### Unity Package

Before a Unity package release:

1. Update `unity-extension/UnitySerializedShield/package.json`.
2. Update `unity-extension/UnitySerializedShield/changelog.md`.
3. Test package installation from disk.
4. Test the migration window in Unity.
5. Confirm `.meta` files are present and stable.

### VS Code Extension

Before a VS Code extension release:

1. Update `vs-code-extension/package.json`.
2. Update `vs-code-extension/CHANGELOG.md`.
3. Run:

```powershell
npm test
```

4. Package:

```powershell
npm run package:vsix
```

5. Upload or publish the generated VSIX.

## What Should Be Tracked

Track:

- Source files.
- Tests.
- Documentation.
- Unity `.meta` files inside the package.
- Unity package manifest files.
- VS Code `package.json` and `package-lock.json`.
- Images and icons used by the extension.

Do not track:

- `node_modules/`
- compiled `out/`
- `.vscode-test/`
- generated `.vsix` files
- Unity `Library/`, `Temp/`, `Obj/`, `Logs/`, `Build/`
- Visual Studio `.vs/`, `bin/`, `obj/`

## Current Project Status

Current implemented parts:

- Unity Package Manager package.
- Unity migration window.
- Unity migration scanner, processor, backup, and restore flow.
- VS Code extension for source-level rename protection.
- VS Code extension development, contribution, publishing, and support docs.

Current placeholder parts:

- Visual Studio extension.

The repository is ready to support all three tool areas, but the Visual Studio extension still needs implementation.
