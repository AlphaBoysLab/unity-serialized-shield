# Contributing to the Visual Studio Extension

This folder contains the Visual Studio VSIX version of UnitySerializedShield.

The extension watches C# editor changes in Visual Studio and adds Unity's `[FormerlySerializedAs]` attribute when it detects a safe `[SerializeField]` rename.

## Requirements

- Visual Studio 2026 or compatible version
- Visual Studio extension development workload
- .NET SDK
- Unity C# files for manual testing

## Solution

Open:

```text
UnitySerializedShield.VisualStudio.slnx
```

Main projects:

- `UnitySerializedShield.Core`: parser and edit-building logic
- `UnitySerializedShield.Core.Tests`: xUnit tests for core behavior
- `UnitySerializedShield.VisualStudio`: VisualStudio.Extensibility VSIX project

## Build and Test

Run tests:

```powershell
dotnet test UnitySerializedShield.VisualStudio.slnx
```

Build a Release VSIX:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  UnitySerializedShield.VisualStudio.slnx `
  /p:Configuration=Release
```

The generated VSIX is:

```text
UnitySerializedShield.VisualStudio\bin\Release\net8.0-windows8.0\UnitySerializedShield.VisualStudio.vsix
```

Do not commit generated `.vsix`, `bin/`, or `obj/` files.

## Debugging

Use the `Visual Studio Experimental Instance` launch profile.

Manual test flow:

1. Start the extension with `F5`.
2. In the Experimental Instance, open a Unity `.cs` file.
3. Confirm `Extensions > UnitySerializedShield: Show Status` appears.
4. Rename a `[SerializeField]` field.
5. Confirm `[FormerlySerializedAs("oldName")]` is inserted.

Example:

```csharp
[SerializeField] private float maxDistance = 100f;
```

After renaming `maxDistance` to `attackDistance`, the extension should produce:

```csharp
using UnityEngine.Serialization;

[FormerlySerializedAs("maxDistance")]
[SerializeField] private float attackDistance = 100f;
```

## Code Guidelines

- Keep Visual Studio API code in `UnitySerializedShield.VisualStudio`.
- Keep parser and rename logic in `UnitySerializedShield.Core`.
- Add xUnit tests for parser/edit behavior changes.
- Preserve conservative behavior when a rename is ambiguous.
- Apply insertions from highest offset to lowest offset.

## Publishing

See `PUBLISHING.md` for VSIX packaging and Visual Studio Marketplace notes.
