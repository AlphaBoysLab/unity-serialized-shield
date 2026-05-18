# UnitySerializedShield Visual Studio Extension

This folder contains the Visual Studio version of the existing VS Code extension.

The VS Code extension already works by:

1. Remembering the previous text of open `.cs` documents.
2. Watching document text changes.
3. Parsing Unity `[SerializeField]` fields before and after the change.
4. Detecting safe field renames.
5. Inserting `[FormerlySerializedAs("oldName")]`.
6. Adding `using UnityEngine.Serialization;` when needed.

The Visual Studio extension keeps the same behavior, but it is built as a Visual Studio VSIX in C#.

## Current Status

Implemented:

- `UnitySerializedShield.Core` contains the C# port of the VS Code parser and edit builder.
- `UnitySerializedShield.Core.Tests` contains xUnit coverage for the core rename behavior.
- `UnitySerializedShield.VisualStudio` contains a VisualStudio.Extensibility VSIX project.
- `SerializedShieldTextViewListener` watches `.cs` text views, compares previous/current text, and applies `[FormerlySerializedAs]` insertions.
- `UnitySerializedShield: Show Status` is available from Visual Studio's Extensions menu.

Still manual to verify:

- Run the VSIX in Visual Studio's experimental instance.
- Open a Unity C# file.
- Rename a `[SerializeField]` field and confirm the attribute is inserted in the editor.

## Workstation Check

Checked on this machine:

- Repository root: `E:\Nodejs\serialized-shield\unity-serialized-shield`
- VS Code extension folder: `vs-code-extension`
- Visual Studio extension folder: `visual-studio-extension`
- .NET SDK installed: `10.0.300`
- Visual Studio installed: `C:\Program Files\Microsoft Visual Studio\18\Community`
- Visual Studio product: Visual Studio Community 2026 / 18.6
- Visual Studio executable: `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe`
- MSBuild executable: `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- VSIX installer: `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe`

Notes:

- `devenv` and `msbuild` may not be on PATH, so the full Visual Studio paths are used in build/debug commands.
- `Microsoft.VisualStudio.Component.VSSDK` is required. If the VSIX project opens and builds, the Visual Studio extension development workload is installed.

## Install Required Visual Studio Workload

Open Visual Studio Installer:

```powershell
Start-Process "C:\Program Files (x86)\Microsoft Visual Studio\Installer\setup.exe"
```

Then:

1. Select Visual Studio Community 2026.
2. Click Modify.
3. Install the `Visual Studio extension development` workload.
4. Apply changes.

After installation, confirm the VSSDK is available:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -products * -requires Microsoft.VisualStudio.Component.VSSDK -property installationPath
```

If it prints the Visual Studio installation path, the SDK is ready.

## Recommended Project Layout

Use a C# solution like this:

```text
visual-studio-extension/
  UnitySerializedShield.VisualStudio.sln
  src/
    UnitySerializedShield.Core/
      UnitySerializedShield.Core.csproj
      TextUtils.cs
      SerializedFieldParser.cs
      FormerlySerializedAsBuilder.cs
    UnitySerializedShield.VisualStudio/
      UnitySerializedShield.VisualStudio.csproj
      source.extension.vsixmanifest
      UnitySerializedShieldExtension.cs
      SerializedShieldTextViewListener.cs
  tests/
    UnitySerializedShield.Core.Tests/
      UnitySerializedShield.Core.Tests.csproj
      FormerlySerializedAsBuilderTests.cs
```

Keep the parser and edit-building logic in `UnitySerializedShield.Core`. This lets both the Visual Studio extension and automated tests use the same logic without depending on Visual Studio APIs.

## Ported VS Code Files

The core logic was ported from:

```text
vs-code-extension/src/textUtils.ts
vs-code-extension/src/serializedFieldParser.ts
vs-code-extension/src/formerlySerializedAs.ts
vs-code-extension/src/test/extension.test.ts
```

C# equivalents:

```text
textUtils.ts              -> TextUtils.cs
serializedFieldParser.ts  -> SerializedFieldParser.cs
formerlySerializedAs.ts   -> FormerlySerializedAsBuilder.cs
extension.test.ts         -> FormerlySerializedAsBuilderTests.cs
```

The VS Code `extension.ts` file was not ported directly. Its role is replaced by `UnitySerializedShield.VisualStudio/SerializedShieldTextViewListener.cs`.

## Create the Solution From Scratch

This is the setup path if the solution ever needs to be recreated from scratch. After the VSSDK workload is installed, create these projects:

```powershell
cd E:\Nodejs\serialized-shield\unity-serialized-shield\visual-studio-extension

dotnet new sln -n UnitySerializedShield.VisualStudio
dotnet new classlib -n UnitySerializedShield.Core -o src\UnitySerializedShield.Core
dotnet new xunit -n UnitySerializedShield.Core.Tests -o tests\UnitySerializedShield.Core.Tests

dotnet sln add src\UnitySerializedShield.Core\UnitySerializedShield.Core.csproj
dotnet sln add tests\UnitySerializedShield.Core.Tests\UnitySerializedShield.Core.Tests.csproj
dotnet add tests\UnitySerializedShield.Core.Tests\UnitySerializedShield.Core.Tests.csproj reference src\UnitySerializedShield.Core\UnitySerializedShield.Core.csproj
```

Create the Visual Studio extension project from Visual Studio:

1. Open Visual Studio.
2. Create a new project.
3. Search for `VisualStudio.Extensibility Project` or `VSIX Project`.
4. Name it `UnitySerializedShield.VisualStudio`.
5. Save it under `visual-studio-extension/src/UnitySerializedShield.VisualStudio`.
6. Add a project reference to `UnitySerializedShield.Core`.

Prefer the newer `VisualStudio.Extensibility Project` template if it is available. It runs out-of-process and is the modern Visual Studio extension model.

## Visual Studio Extension Behavior

The Visual Studio extension needs equivalents for the VS Code activation code:

| VS Code behavior | Visual Studio equivalent |
| --- | --- |
| `onLanguage:csharp` | Register an editor listener for C# text views/documents |
| `workspace.textDocuments` snapshot map | Dictionary keyed by document URI/path |
| `onDidOpenTextDocument` | Text view/document opened listener |
| `onDidChangeTextDocument` | Text view/document changed listener |
| `WorkspaceEdit.insert` | Visual Studio editor text edit API |
| `showInformationMessage` command | Optional Visual Studio command |

The core loop should remain:

```csharp
if document is not C#:
    return

previousText = snapshots[documentPath]
currentText = get current document text
snapshots[documentPath] = currentText

insertions = FormerlySerializedAsBuilder.Build(previousText, currentText)

if insertions has items:
    apply insertions to current document
    snapshots[documentPath] = get updated document text
```

Apply insertions from highest offset to lowest offset if the API uses absolute offsets. This prevents earlier insertions from shifting later offsets.

## Core Test Cases To Port

Start by matching the existing VS Code tests:

1. Adds `[FormerlySerializedAs]` when a `[SerializeField]` variable is renamed.
2. Adds `using UnityEngine.Serialization;` if missing.
3. Does not add duplicate `[FormerlySerializedAs]`.
4. Ignores non-serialized variables.

Then add more Visual Studio-safe parser tests:

1. Attribute above field:

```csharp
[SerializeField]
private float maxDistance = 100f;
```

2. Multiple attributes:

```csharp
[Header("Movement")]
[SerializeField]
private float maxDistance = 100f;
```

3. Static and const fields should be skipped.
4. Multi-field declarations should be skipped.

## Build and Test

Run core tests:

```powershell
dotnet test E:\Nodejs\serialized-shield\unity-serialized-shield\visual-studio-extension\UnitySerializedShield.VisualStudio.sln
```

Build the VSIX with Visual Studio MSBuild:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  E:\Nodejs\serialized-shield\unity-serialized-shield\visual-studio-extension\UnitySerializedShield.VisualStudio.sln `
  /p:Configuration=Release
```

Install the generated VSIX locally:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe" path\to\UnitySerializedShield.VisualStudio.vsix
```

For debugging, use Visual Studio's experimental instance. The Visual Studio SDK templates normally configure this automatically.

## Suggested Development Order

1. Install the Visual Studio extension development workload. Done.
2. Create `UnitySerializedShield.Core`. Done.
3. Port the parser and edit builder from TypeScript to C#. Done.
4. Port the VS Code tests to xUnit and make them pass. Done.
5. Create the Visual Studio VSIX project. Done.
6. Add the editor open/change listener. Done.
7. Connect the listener to `UnitySerializedShield.Core`. Done.
8. Test in the Visual Studio experimental instance with a Unity C# file.
9. Package and install the `.vsix`.

## Useful References

- Visual Studio extension anatomy: https://learn.microsoft.com/en-us/visualstudio/extensibility/vsix/get-started/extension-anatomy?view=vs-2022
- Create a VisualStudio.Extensibility extension: https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/get-started/create-your-first-extension?view=vs-2022
- Work with text in the Visual Studio editor: https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/editor/walkthroughs/working-with-text?view=vs-2022
- Change text in the Visual Studio editor: https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/editor/walkthroughs/editing-text?view=visualstudio
- Visual Studio extensibility model choices: https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/extensibility-models?view=visualstudio
