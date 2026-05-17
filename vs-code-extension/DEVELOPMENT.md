# UnitySerializedShield Development Guide

UnitySerializedShield is a VS Code extension for Unity C# projects. Its main job is to protect Unity Inspector values when a serialized field is renamed.

Example:

```csharp
[SerializeField] private float maxDistance = 100f;
```

If the field is renamed to `attackDistance`, the extension automatically adds Unity's official rename migration attribute:

```csharp
using UnityEngine.Serialization;

[FormerlySerializedAs("maxDistance")]
[SerializeField] private float attackDistance = 100f;
```

This lets Unity keep the old serialized Inspector value and reconnect it to the new field name.

## Requirements

Install dependencies once:

```powershell
npm install
```

Useful scripts:

```powershell
npm run compile
npm run lint
npm test
```

## Project Structure

```text
src/
  extension.ts
  formerlySerializedAs.ts
  serializedFieldParser.ts
  textUtils.ts
  test/
    extension.test.ts
```

`src/extension.ts`

This is the VS Code extension entry point. It listens for C# document changes, keeps a snapshot of the previous document text, asks the rename logic for edits, and applies those edits in VS Code.

`src/formerlySerializedAs.ts`

This file contains the main rename protection logic. It compares the previous version of a file with the current version and builds text insertions for:

- `[FormerlySerializedAs("oldName")]`
- `using UnityEngine.Serialization;`

`src/serializedFieldParser.ts`

This parser finds Unity serialized fields in C# files. It currently supports normal single-field declarations such as:

```csharp
[SerializeField] private float maxDistance = 100f;
```

It intentionally skips multi-field declarations such as:

```csharp
[SerializeField] private int a, b;
```

Skipping ambiguous declarations is safer because the extension should not guess the wrong field.

`src/textUtils.ts`

Small shared helpers live here, such as line splitting, comment stripping, line-ending detection, and C# string escaping.

`src/test/extension.test.ts`

Automated tests for the rename behavior live here.

## How The Extension Works

The extension does not directly talk to Unity or modify Unity Inspector data. It edits the C# script so Unity can migrate the serialized value.

Workflow:

1. A C# document is opened in VS Code.
2. The extension stores the current text as a snapshot.
3. The user renames a serialized field manually or through VS Code rename.
4. VS Code fires a document change event.
5. The extension compares the old snapshot with the new text.
6. If it finds that a `[SerializeField]` field kept the same declaration shape but changed name, it treats that as a field rename.
7. The extension inserts `[FormerlySerializedAs("oldName")]` above the field.
8. If needed, it also inserts `using UnityEngine.Serialization;`.
9. The new document text becomes the latest snapshot.

## Automated Testing

Run:

```powershell
npm test
```

This runs:

```powershell
npm run compile
npm run lint
vscode-test
```

Expected result:

```text
UnitySerializedShield
  pass adds FormerlySerializedAs when a SerializeField variable is renamed
  pass does not add a duplicate FormerlySerializedAs attribute
  pass ignores non-serialized variables
```

Use automated tests when changing:

- rename detection logic
- serialized field parsing
- text insertion positions
- duplicate attribute prevention
- using statement insertion

## Manual Testing During Development

Manual testing is useful because this extension reacts to real VS Code document changes.

1. Open this extension project in VS Code.
2. Press `F5`.
3. A new window named `Extension Development Host` will open.
4. In that new window, open a Unity C# script or create a temporary `.cs` file.
5. Add this code:

```csharp
using UnityEngine;

public class UnitySerializedShieldManualTest : MonoBehaviour
{
    [SerializeField] private float maxDistance = 100f;
}
```

6. Rename `maxDistance` to `attackDistance`.
7. The extension should update the file to:

```csharp
using UnityEngine;
using UnityEngine.Serialization;

public class UnitySerializedShieldManualTest : MonoBehaviour
{
    [FormerlySerializedAs("maxDistance")]
    [SerializeField] private float attackDistance = 100f;
}
```

You can rename manually by typing, or by using VS Code's rename shortcut.

On Windows:

```text
F2
```

Then enter the new field name.

## Status Command

The extension contributes this command:

```text
UnitySerializedShield: Show Status
```

Open the Command Palette:

```text
Ctrl + Shift + P
```

Run `UnitySerializedShield: Show Status`.

If the extension is active, VS Code should show:

```text
UnitySerializedShield is watching Unity serialized field renames.
```

This command is only for checking that the extension is loaded. The extension's actual rename protection runs automatically.

## Good Manual Test Cases

Single-line attribute:

```csharp
[SerializeField] private float maxDistance = 100f;
```

Attribute above field:

```csharp
[SerializeField]
private float maxDistance = 100f;
```

Multiple attributes:

```csharp
[Header("Movement")]
[SerializeField] private float maxDistance = 100f;
```

File already has the required using:

```csharp
using UnityEngine;
using UnityEngine.Serialization;
```

The extension should not add a duplicate using.

Field already has the old name attribute:

```csharp
[FormerlySerializedAs("maxDistance")]
[SerializeField] private float attackDistance = 100f;
```

The extension should not add a duplicate attribute.

Non-serialized field:

```csharp
private float maxDistance = 100f;
```

The extension should ignore this field.

## Current Limitations

This extension uses text-based parsing instead of a full C# compiler or Roslyn parser. That keeps the extension simple, but it also means it intentionally handles the common Unity field declaration style first.

Currently supported:

- `[SerializeField] private float maxDistance = 100f;`
- `[SerializeField]` on the line above the field
- multiple attribute lines above a field
- normal single-field declarations

Currently skipped:

- multi-field declarations, such as `private int a, b;`
- complex generated code
- cases where the type, initializer, and field name all change at the same time

When adding support for more C# patterns, add tests first in `src/test/extension.test.ts`.

## Development Checklist

Before finishing a change:

1. Run compile:

```powershell
npm run compile
```

2. Run lint:

```powershell
npm run lint
```

3. Run tests:

```powershell
npm test
```

4. Press `F5` and do one manual rename test in the Extension Development Host.

## Debugging Tips

If the extension does not react:

- Make sure the file is a `.cs` file.
- Make sure the field has `[SerializeField]`.
- Make sure you are testing inside the `Extension Development Host` window.
- Run `UnitySerializedShield: Show Status` from the Command Palette.
- Check the Debug Console in the original VS Code window.

If the attribute is not inserted:

- Check whether the declaration is a single field declaration.
- Check whether the old and new field declarations still have the same type and initializer.
- Check whether `[FormerlySerializedAs("oldName")]` already exists.

If automated tests fail:

- Run `npm run compile` first to catch TypeScript errors.
- Check recent changes in `formerlySerializedAs.ts` and `serializedFieldParser.ts`.
- Add a small focused test case before changing parser behavior.
