![UnitySerializedShield](https://raw.githubusercontent.com/AlphaBoysLab/unity-serialized-shield/main/Images/unity-serialized-shield.png)

# UnitySerializedShield (Roslyn)

Rider-like protection for Unity serialized field renames — for Visual Studio.

When you rename a Unity `[SerializeField]` (or public) field, Unity normally loses the link between the old serialized data and the new field name, so values set in the Inspector, prefabs, scenes, and ScriptableObjects can be lost. UnitySerializedShield fixes this automatically: on a real **Rename Symbol** it adds Unity's official `[FormerlySerializedAs("oldName")]` attribute to the field **declaration**, so Unity reconnects the data.

## What makes this version different

This is the Roslyn-powered, solution-wide engine. Unlike text-only approaches, it understands your code semantically:

- **Rename from anywhere.** Start the rename on the declaration *or* on a reference in another file/class — the `[FormerlySerializedAs]` still lands on the field's declaration.
- **Semantic detection.** Built on Roslyn (`VisualStudioWorkspace`), not regular expressions.
- **Rename, not typing.** Only reacts to the actual Rename Symbol command (Ctrl+R, R / F2), never to ordinary character-by-character typing.
- **Conservative by design.** Skips ambiguous cases (static/const/readonly, `[NonSerialized]`, multi-field declarations, non-Unity types) rather than risk a wrong attribute.

## How to use

1. Put the caret on a serialized field and run **Rename** (`Ctrl+R, R`).
2. Type the new name and commit.
3. UnitySerializedShield adds `[FormerlySerializedAs("oldName")]` above the field and inserts `using UnityEngine.Serialization;` if needed.

### Example

Before — rename `maxDistance` to `attackDistance`:

```csharp
using UnityEngine;

public sealed class EnemySensor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 100f;
}
```

After:

```csharp
using UnityEngine;
using UnityEngine.Serialization;

public sealed class EnemySensor : MonoBehaviour
{
    [FormerlySerializedAs("maxDistance")]
    [SerializeField] private float attackDistance = 100f;
}
```

Unity now migrates the old serialized value to the new field name.

## Recommended workflow

For the safest migration, also install the **UnitySerializedShield** Unity package inside Unity. After your renames add `[FormerlySerializedAs]`, run the in-Unity migration workflow so serialized data is migrated, then completed attributes can be cleaned up automatically.

> Do not manually remove `[FormerlySerializedAs]` attributes before Unity has migrated the data — that is the link Unity uses to move the old value to the new name.

## Links

- Repository: https://github.com/AlphaBoysLab/unity-serialized-shield
- Issues / Q&A: use the repository's issue tracker

## License

Released under the MIT License.
