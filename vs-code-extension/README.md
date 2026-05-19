# UnitySerializedShield

UnitySerializedShield is a Visual Studio Code extension for Unity C# projects. It helps protect Unity Inspector values when serialized fields are renamed.

When a `[SerializeField]` field is renamed, UnitySerializedShield automatically adds Unity's `[FormerlySerializedAs]` migration attribute so Unity can reconnect the old serialized value to the new field name.

## Important Unity Migration Notice

This extension protects the C# rename step, but Unity serialized data still needs to be migrated inside Unity. Do not manually remove all `[FormerlySerializedAs]` attributes before Unity has migrated the data. Removing them too early can cause Unity Inspector values, prefab references, scene references, or ScriptableObject data to be lost.

For the safest workflow, also install the UnitySerializedShield Unity package from this repository:

```text
unity-extension/UnitySerializedShield/package.json
```

Install it in Unity with `Window > Package Manager > + > Add package from disk...`, then select the package file above. You can also install from Git:

```text
https://github.com/AlphaBoysLab/unity-serialized-shield.git?path=unity-extension/UnitySerializedShield
```

After renaming fields, open Unity and run the SerializedShield migration workflow. The Unity package migrates serialized data and can remove completed `[FormerlySerializedAs]` attributes after references are preserved.

For full setup and migration instructions, visit the GitHub repository: [AlphaBoysLab/unity-serialized-shield](https://github.com/AlphaBoysLab/unity-serialized-shield)

## Features

- Watches C# files in Unity projects.
- Detects common `[SerializeField]` field renames.
- Inserts `[FormerlySerializedAs("oldName")]` above the renamed field.
- Adds `using UnityEngine.Serialization;` when the file needs it.
- Avoids adding duplicate migration attributes.

Example:

```csharp
using UnityEngine;

public class EnemySensor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 100f;
}
```

After renaming `maxDistance` to `attackDistance`, UnitySerializedShield updates the script:

```csharp
using UnityEngine;
using UnityEngine.Serialization;

public class EnemySensor : MonoBehaviour
{
    [FormerlySerializedAs("maxDistance")]
    [SerializeField] private float attackDistance = 100f;
}
```

## Requirements

- Visual Studio Code
- A Unity C# project
- C# scripts that use Unity serialized fields

UnitySerializedShield edits C# source files only. It does not modify Unity assets or scene files directly.

## Usage

1. Open a Unity project in Visual Studio Code.
2. Rename a C# field marked with `[SerializeField]`.
3. UnitySerializedShield adds the migration attribute automatically when it recognizes a safe rename.

You can also run `UnitySerializedShield: Show Status` from the Command Palette to confirm the extension is active.

## Install From VSIX

If you download a `.vsix` file from a GitHub Release:

1. Open Visual Studio Code.
2. Open the Extensions view.
3. Select `Views and More Actions...`.
4. Select `Install from VSIX...`.
5. Choose the downloaded `unity-serialized-shield-*.vsix` file.

You can also install from the command line:

```powershell
code --install-extension unity-serialized-shield-1.0.3.vsix
```

## Supported Patterns

UnitySerializedShield focuses on common Unity field declarations:

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

## Known Limitations

- Multi-field declarations such as `private int a, b;` are skipped.
- Static and const fields are skipped.
- Complex generated code may not be recognized.
- If the field type, initializer, and name all change at the same time, the extension may skip the edit.

These cases are skipped intentionally so the extension does not guess incorrectly.

## Development

Install dependencies:

```powershell
npm install
```

Compile the extension:

```powershell
npm run compile
```

Run linting and automated tests:

```powershell
npm test
```

Package a local VSIX:

```powershell
npm run package:vsix
```

The generated file will look like:

```text
unity-serialized-shield-1.0.3.vsix
```

## Contributing

UnitySerializedShield is open source under the MIT license. Contributions are welcome.

Good first contribution areas include:

- Adding parser tests for more Unity field declaration styles.
- Improving rename detection while keeping safe skip behavior.
- Improving documentation and examples.
- Testing the extension in real Unity projects and reporting edge cases.

Before opening a pull request:

1. Create a feature branch from the latest main branch.
2. Keep the change focused on one fix or feature.
3. Add or update tests when behavior changes.
4. Run `npm test`.
5. Update documentation if the user-facing behavior changes.

For larger changes, please open an issue first and describe the problem you want to solve.

## GitHub Releases

To attach a VSIX to a GitHub Release manually:

1. Update `version` in `package.json`.
2. Update `CHANGELOG.md`.
3. Run `npm test`.
4. Run `npm run package:vsix`.
5. Commit your changes and push to GitHub.
6. Open your GitHub repository.
7. Go to `Releases`.
8. Select `Draft a new release`.
9. Create a tag such as `v1.0.3`.
10. Attach `unity-serialized-shield-1.0.3.vsix`.
11. Publish the release.

You can also create a release with the GitHub CLI:

```powershell
gh release create v1.0.3 .\unity-serialized-shield-1.0.3.vsix --title "UnitySerializedShield 1.0.3" --notes "Release 1.0.3."
```

This repository also includes a GitHub Actions workflow that can build and upload the VSIX to a release automatically when a GitHub Release is published.

## Release Notes

### 0.0.1

Initial release of UnitySerializedShield.
