# UnitySerializedShield

UnitySerializedShield helps Unity developers safely rename serialized C# fields without losing values already assigned in the Inspector, prefabs, scenes, or ScriptableObjects.

When a Unity field marked with `[SerializeField]` is renamed, Unity no longer knows that the old serialized data belongs to the new field name. The normal fix is to add Unity's `[FormerlySerializedAs]` attribute before Unity reloads and drops the connection. UnitySerializedShield automates that protection in the editor tools developers already use.

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

## Example

Before rename:

```csharp
using UnityEngine;

public sealed class EnemySensor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 100f;
}
```

After renaming `maxDistance` to `attackDistance`, UnitySerializedShield adds the migration attribute:

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

The Unity package provides Editor tooling for migration workflows inside Unity.

Folder:

```text
unity-extension/UnitySerializedShield
```

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
