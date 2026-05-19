# Unity Serialized Shield

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Unity](https://img.shields.io/badge/Unity-6000.3%2B-blue.svg)](https://unity.com/)

Unity Serialized Shield is a Unity Editor tool for safely migrating renamed serialized fields. It finds scripts that use `FormerlySerializedAs`, previews referenced serialized assets, reserializes the selected scenes, prefabs, and assets, and can then remove completed migration attributes.

## Features

- Scans project and package scripts for `FormerlySerializedAs` attributes.
- Previews serialized scene, prefab, and asset files that reference a selected script.
- Reserializes matching Unity assets so renamed fields keep their data.
- Creates migration backups before changes when the backup option is enabled.
- Restores the latest backup session from the migration window.
- Optionally removes completed `FormerlySerializedAs` attributes after migration.

## Installation

The package manifest name is:

```text
com.alphaboyslab.unity-serialized-shield
```

### Install From A Local Folder

1. Open Unity.
2. Go to `Window > Package Manager`.
3. Click the `+` button.
4. Select `Add package from disk...`.
5. Select this package's `package.json` file.
6. Click `Open`.

This is the best option while developing or testing the package locally.

### Install From A Git URL

1. Open Unity.
2. Go to `Window > Package Manager`.
3. Click the `+` button.
4. Select `Add package from git URL...`.
5. Enter the Git URL for this package.
6. Click `Add`.

Example:

```text
https://github.com/AlphaBoysLab/unity-serialized-shield
```

If this package is inside a repository subfolder, add the package path:

```text
https://github.com/AlphaBoysLab/unity-serialized-shield.git?path=unity-extension/UnitySerializedShield
```

### Install Through manifest.json

Open your Unity project's `Packages/manifest.json` file and add the package under `dependencies`:

```json
{
  "dependencies": {
    "com.alphaboyslab.unity-serialized-shield": "https://github.com/AlphaBoysLab/unity-serialized-shield/tree/main/unity-extension/UnitySerializedShield"
  }
}
```

## Usage

1. Make sure your project is committed or otherwise backed up.
2. Rename a serialized field and add Unity's `FormerlySerializedAs` attribute with the previous field name.
3. Open `Tools > SerializedShield > Migration Window`.
4. Click `Scan`.
5. Use `Preview References` to inspect affected serialized files.
6. Choose the migration options you need.
7. Run `Migrate / Serialize` for one script or `Migrate All Listed Scripts` for the current filtered list.
8. Review the result and commit the changed serialized assets.

## Package Structure

```text
UnitySerializedShield/
|-- Editor/          # Editor-only migration tools
|-- package.json     # Unity Package Manager manifest
|-- readme.md        # Package documentation
|-- changelog.md     # Release notes
|-- license.md       # MIT license
```

## Requirements

- Unity 6000.3 or newer.
- Editor usage only; this package is not intended for runtime builds.

## Notes

Use version control before running migrations. Serialized asset migrations can touch many scenes, prefabs, and `.asset` files depending on project references.
