# Publishing UnitySerializedShield

Use these steps after creating your Visual Studio Marketplace publisher.

## Required Account Values

You need:

- A Visual Studio Marketplace publisher ID.
- An Azure DevOps Personal Access Token with Marketplace Manage scope.
- Optional: a public repository URL for the `repository` field in `package.json`.

## One-Time Project Setup

The publisher ID is already set in `package.json`:

```json
"publisher": "alphaboyslab"
```

If you have a public repository, also add:

```json
"repository": {
  "type": "git",
  "url": "https://github.com/your-user/unity-serialized-shield.git"
}
```

## Package

```powershell
npm run package:vsix
```

This creates:

```text
unity-serialized-shield-1.0.3.vsix
```

## Publish From Terminal

Log in once:

```powershell
npm exec vsce -- login alphaboyslab
```

Then publish:

```powershell
npm run publish:vsce
```

## Manual Upload

You can also upload the VSIX manually:

1. Open https://marketplace.visualstudio.com/manage/publishers/
2. Select your publisher.
3. Choose the option to add/upload an extension.
4. Upload `unity-serialized-shield-1.0.3.vsix`.

## Before Each Release

1. Update `version` in `package.json`.
2. Update `CHANGELOG.md`.
3. Run `npm test`.
4. Run `npm run package:vsix`.

## GitHub Release With VSIX

Manual release:

1. Commit and push the release changes.
2. Open the GitHub repository.
3. Go to `Releases`.
4. Select `Draft a new release`.
5. Create a tag such as `v1.0.3`.
6. Attach `unity-serialized-shield-1.0.3.vsix`.
7. Publish the release.

GitHub CLI release:

```powershell
gh release create v1.0.3 .\unity-serialized-shield-1.0.3.vsix --title "UnitySerializedShield 1.0.3" --notes "Release 1.0.3."
```

Automatic release asset upload:

The `.github/workflows/release-vsix.yml` workflow builds and uploads the VSIX when a GitHub Release is published. If you publish a release before attaching a VSIX, GitHub Actions will create the VSIX and upload it to that release automatically.
