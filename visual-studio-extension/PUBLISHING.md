# Publishing UnitySerializedShield for Visual Studio

## Release Build

Build the Release VSIX from Visual Studio:

1. Open `UnitySerializedShield.VisualStudio.slnx`.
2. Set configuration to `Release`.
3. Select `Build > Rebuild Solution`.

The generated VSIX is:

```text
UnitySerializedShield.VisualStudio\bin\Release\net8.0-windows8.0\UnitySerializedShield.VisualStudio.vsix
```

## Icon

The Visual Studio extension uses the same icon as the VS Code extension:

```text
UnitySerializedShield.VisualStudio\Images\icon.png
```

It is copied from:

```text
..\vs-code-extension\images\icon.png
```

The generated VSIX manifest contains:

```xml
<Icon>Images/icon.png</Icon>
```

## Marketplace Checklist

Before publishing:

1. Update version metadata in `ExtensionEntrypoint.cs`.
2. Build `Release`.
3. Test the `.vsix` in Visual Studio Experimental Instance.
4. Test installing the `.vsix` normally with `VSIXInstaller.exe`.
5. Review `publishmanifest.json`.
6. Upload the VSIX to the Visual Studio Marketplace.

## Marketplace Publisher

The `publisher` value in `publishmanifest.json` must match your Visual Studio Marketplace publisher ID. If your actual publisher ID is different from `AlphaBoysLab`, update it before uploading.

## Generated VSIX

Current release output path:

```text
E:\Nodejs\serialized-shield\unity-serialized-shield\visual-studio-extension\UnitySerializedShield.VisualStudio\bin\Release\net8.0-windows8.0\UnitySerializedShield.VisualStudio.vsix
```
