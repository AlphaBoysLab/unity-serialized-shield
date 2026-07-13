# UnitySerializedShield for Visual Studio

UnitySerializedShield helps Unity developers safely rename serialized C# fields from Visual Studio without losing values already assigned in the Unity Inspector, prefabs, scenes, or ScriptableObjects.

When you rename a Unity field marked with `[SerializeField]`, Unity normally needs `[FormerlySerializedAs]` to understand that the old serialized data belongs to the new field name. UnitySerializedShield adds that migration attribute automatically when you use Visual Studio's real rename command.

## Why Use It

Unity stores serialized values by field name. If a serialized field is renamed without migration metadata, Unity can lose the connection to existing Inspector values.

UnitySerializedShield helps protect:

- Inspector values on scene objects.
- Prefab references and tuned prefab values.
- ScriptableObject configuration data.
- Serialized gameplay, UI, enemy, level, and balancing fields.

## Important Unity Migration Notice

This Visual Studio extension protects the C# rename step, but Unity serialized data still needs to be migrated inside Unity.

Do not manually remove all `[FormerlySerializedAs]` attributes before Unity has migrated the data. Removing them too early can cause Unity Inspector values, prefab references, scene references, or ScriptableObject data to be lost.

For the safest workflow, also install the UnitySerializedShield Unity package from the GitHub repository:

```text
unity-extension/UnitySerializedShield/package.json
```

Install it in Unity:

1. Open your Unity project.
2. Go to `Window > Package Manager`.
3. Click the `+` button.
4. Choose `Add package from disk...`.
5. Select `unity-extension/UnitySerializedShield/package.json`.

You can also install the Unity package from Git:

```text
https://github.com/AlphaBoysLab/unity-serialized-shield.git?path=unity-extension/UnitySerializedShield
```

After renaming fields, open Unity and run the SerializedShield migration workflow. The Unity package migrates serialized data and can remove completed `[FormerlySerializedAs]` attributes after references are preserved.

For full setup and migration instructions, visit the GitHub repository: [AlphaBoysLab/unity-serialized-shield](https://github.com/AlphaBoysLab/unity-serialized-shield)

## How It Works

Rename a serialized field in Visual Studio using the menu rename command or `Ctrl+R`.

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

## Features

- Detects safe Unity `[SerializeField]` field renames.
- Adds `[FormerlySerializedAs("oldName")]` above the renamed field.
- Protects `[field: SerializeField]` auto-properties with `[field: FormerlySerializedAs("<OldName>k__BackingField")]`.
- Adds `using UnityEngine.Serialization;` when needed.
- Avoids duplicate migration attributes.
- Ignores normal typing and only reacts to the real Rename Symbol command.
- Skips ambiguous cases (delete+add, reorder, mixed edits) instead of guessing.
- Only runs in projects that reference UnityEngine; other C# solutions are never touched.
- Never force-saves a document that has unsaved changes.

## Disabling the Extension

To turn the extension off without uninstalling it, create the DWORD registry value:

```text
HKEY_CURRENT_USER\Software\UnitySerializedShield
    Enabled = 0
```

Set it back to `1` (or delete the value) to re-enable. The setting is picked up within about 30 seconds — no restart needed.

## Recommended Workflow

1. Install this Visual Studio extension.
2. Install the UnitySerializedShield Unity package in your Unity project.
3. Rename serialized fields in Visual Studio with Rename or `Ctrl+R`.
4. Let the extension add `[FormerlySerializedAs("oldName")]`.
5. Open Unity and run the SerializedShield migration workflow.
6. Let the Unity package migrate serialized assets and clean completed migration attributes.

## Supported Field Patterns

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

## Safe Skip Behavior

The extension intentionally skips ambiguous cases rather than adding a wrong migration attribute.

Skipped examples include:

- Non-serialized fields.
- Static fields.
- Const fields.
- Multi-field declarations such as `private int a, b;`.
- Cases where the field type, initializer, and name all change at the same time.

## Learn More

Documentation, source code, Unity package instructions, and issue tracking are available on GitHub:

[AlphaBoysLab/unity-serialized-shield](https://github.com/AlphaBoysLab/unity-serialized-shield)

## License

UnitySerializedShield is released under the MIT License.
