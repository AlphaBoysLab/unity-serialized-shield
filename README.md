![UnitySerializedShield](Images/unity-serialized-shield.png)

# UnitySerializedShield

UnitySerializedShield helps Unity developers safely rename serialized C# fields without losing values already assigned in the Inspector, prefabs, scenes, or ScriptableObjects.

When a Unity field marked with `[SerializeField]` is renamed, Unity no longer knows that the old serialized data belongs to the new field name. The normal fix is to add Unity's `[FormerlySerializedAs]` attribute before Unity reloads and drops the connection. UnitySerializedShield automates that protection in the editor tools developers already use.

## Available In Your Editor

UnitySerializedShield is available from both extension marketplaces:

- **Visual Studio Marketplace**: install the Visual Studio extension for Unity projects opened in Visual Studio.
- **Visual Studio Code Marketplace**: install the VS Code extension for Unity projects opened in Visual Studio Code.

The easiest way to install is to open your editor's extension manager and search for:

```text
UnitySerializedShield
```

After installation, keep working normally. When you use the editor's real rename command on a Unity serialized field, UnitySerializedShield adds the correct `[FormerlySerializedAs]` attribute for you.

## Quick Example

Rename a serialized field with your editor rename tool:

- Visual Studio: use **Rename** from the menu or press `Ctrl+R`.
- Visual Studio Code: use **Rename Symbol** from the menu or press `F2`.

Before rename:

```csharp
using UnityEngine;

public sealed class EnemySensor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 100f;
}
```

After renaming `maxDistance` to `attackDistance`, UnitySerializedShield updates the script:

```csharp
using UnityEngine;
using UnityEngine.Serialization;

public sealed class EnemySensor : MonoBehaviour
{
    [FormerlySerializedAs("maxDistance")]
    [SerializeField] private float attackDistance = 100f;
}
```

Unity can now reconnect the old serialized value to the new field name.

## Important: Do Not Manually Remove Migration Attributes

After a rename, `[FormerlySerializedAs]` is the link Unity uses to move the old serialized value to the new field name. If you remove all `[FormerlySerializedAs]` attributes before Unity has migrated the data, Unity can lose Inspector references and serialized values in scenes, prefabs, and ScriptableObjects.

For the safest workflow, install the UnitySerializedShield package inside Unity too. In this GitHub repository, the Unity package is not at the repository root. It is inside this subfolder:

```text
unity-extension/UnitySerializedShield
```

That folder contains the Unity Package Manager file:

```text
unity-extension/UnitySerializedShield/package.json
```

To install it from a downloaded copy of this repository:

1. Open your Unity project.
2. Go to `Window > Package Manager`.
3. Click the `+` button.
4. Choose `Add package from disk...`.
5. Select `unity-extension/UnitySerializedShield/package.json` from this repository.
6. Click `Open`.

To install it directly from Git, use the repository URL with the package subfolder path:

```text
https://github.com/AlphaBoysLab/unity-serialized-shield.git?path=unity-extension/UnitySerializedShield
```

For the full Unity-side setup, usage, backup, and migration instructions, read the Unity package README:

```text
unity-extension/UnitySerializedShield/readme.md
```

The Unity package is made for the migration step inside Unity. When you run the migration workflow, it updates Unity serialized data so references stay connected, then it can remove the completed `[FormerlySerializedAs]` fields from your scripts automatically.

![SerializedShield Migration](Images/SerializedShield%20Migration.png)

Recommended workflow:

1. Rename serialized fields in Visual Studio or VS Code.
2. Let the editor extension add `[FormerlySerializedAs("oldName")]`.
3. Open Unity with the UnitySerializedShield package installed.
4. Run the SerializedShield migration workflow.
5. Let the Unity package migrate the data and clean completed migration attributes.

## Why This Exists

Unity developers rename variables all the time during normal development:

- `speed` becomes `moveSpeed`
- `damage` becomes `attackDamage`
- `maxDistance` becomes `detectionRange`
- `healthBar` becomes `playerHealthBar`

In regular C# code, a rename is usually safe. In Unity, serialized fields are different because values are stored by field name inside Unity assets. If a serialized field is renamed without migration metadata, Unity can lose the Inspector value.

That can mean:

- prefab references become missing
- scene objects lose tuned values
- ScriptableObject configs reset
- designers need to re-enter data manually
- bugs appear only after entering Play Mode or opening a scene

UnitySerializedShield was created to reduce that day-to-day risk for Unity teams. It gives developers a small safety layer while refactoring gameplay scripts, UI controllers, enemy configs, level data, and other serialized MonoBehaviour or ScriptableObject fields.

## How It Helps Unity Developers

UnitySerializedShield is useful during everyday Unity work:

- Refactoring gameplay scripts without losing prefab tuning.
- Cleaning up variable names before release.
- Renaming fields after a designer has already configured values in the Inspector.
- Protecting ScriptableObject balancing data.
- Reducing accidental data loss during code review changes.
- Making field rename migrations visible in source control.

The tool does not modify scenes, prefabs, or assets directly in the code editor extensions. It edits C# source files by adding Unity's official migration attribute.

## Repository Contents

This repository contains three related parts:

```text
vs-code-extension/
visual-studio-extension/
unity-extension/
```

### VS Code Extension

The VS Code extension watches C# document edits. When it detects a safe `[SerializeField]` rename, it inserts `[FormerlySerializedAs]` automatically.

Folder:

```text
vs-code-extension
```

### Visual Studio Extension

The Visual Studio extension provides the same rename protection for Visual Studio users. It is packaged as a `.vsix`.

Folder:

```text
visual-studio-extension
```

Release VSIX output:

```text
visual-studio-extension/UnitySerializedShield.VisualStudio/bin/Release/net8.0-windows8.0/UnitySerializedShield.VisualStudio.vsix
```

### Unity Editor Extension

The Unity package provides Editor tooling for migration workflows inside Unity. Use it after code-editor renames so Unity serialized data is migrated safely before completed `[FormerlySerializedAs]` attributes are removed.

Folder:

```text
unity-extension/UnitySerializedShield
```

Important files:

```text
unity-extension/UnitySerializedShield/package.json
unity-extension/UnitySerializedShield/readme.md
```

Follow the deeper instructions in `unity-extension/UnitySerializedShield/readme.md` before running migrations in a real Unity project.

## Supported Field Patterns

UnitySerializedShield focuses on common safe field declarations:

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

## Safe Skip Behavior

The project intentionally skips ambiguous cases rather than guessing incorrectly.

Skipped examples include:

- non-serialized fields
- static fields
- const fields
- multi-field declarations such as `private int a, b;`
- cases where the type, initializer, and name all change at the same time

This conservative behavior is important because a wrong migration attribute can be worse than no automatic edit.

## Development

VS Code extension:

```powershell
cd vs-code-extension
npm install
npm test
```

Visual Studio extension:

```powershell
cd visual-studio-extension
dotnet test UnitySerializedShield.VisualStudio.slnx
```

Unity package:

```text
Open Unity Package Manager > Add package from disk > unity-extension/UnitySerializedShield/package.json
```

## Contributing

See:

```text
CONTRIBUTING.md
vs-code-extension/CONTRIBUTING.md
visual-studio-extension/CONTRIBUTING.md
unity-extension/CONTRIBUTING.md
```

## Creator

Created by **Md Shahriar Islam** for Unity developers and teams who want safer day-to-day refactoring.

- Email: `shahriar.islam.dev@gmail.com`
- Location: Dhaka, Bangladesh
- Company LinkedIn: https://linkedin.com/company/alphaboyslab
- Founder LinkedIn: https://linkedin.com/in/shahriar-softwareengineer

## License

UnitySerializedShield is released under the MIT License.
