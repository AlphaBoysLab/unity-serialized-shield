# Contributing to UnitySerializedShield

Thanks for helping improve UnitySerializedShield. This repository contains related tools for protecting Unity serialized data when C# fields are renamed.

## Repository Areas

- `vs-code-extension/`: Visual Studio Code extension written in TypeScript.
- `visual-studio-extension/`: Visual Studio VSIX extension written in C#.
- `unity-extension/`: Unity Editor package and migration window.
- `docs/`: Shared notes and project information.

## Development Principles

- Preserve Unity serialized data safety first.
- Prefer safe skip behavior over guessing when a rename is ambiguous.
- Keep changes focused to one tool or behavior at a time when possible.
- Add or update tests when parser, rename detection, or migration behavior changes.
- Do not commit generated packages such as `.vsix`, `bin/`, `obj/`, `Library/`, or `Temp/`.

## Setup

Clone the repository, then work in the folder for the tool you are changing.

For VS Code extension work:

```powershell
cd vs-code-extension
npm install
npm test
```

For Visual Studio extension work:

```powershell
cd visual-studio-extension
dotnet test UnitySerializedShield.VisualStudio.slnx
```

For Unity package work, open a Unity project that references the package folder under:

```text
unity-extension/UnitySerializedShield
```

## Pull Request Guidelines

- Describe the user-facing problem and the fix.
- Include manual test steps for editor/extension behavior.
- Include automated test results when available.
- Update README, publishing docs, or changelog files when behavior changes.
- Keep generated build artifacts out of the pull request.

## Testing Expectations

At minimum, test the area you changed:

- VS Code extension: `npm test`
- Visual Studio extension: `dotnet test UnitySerializedShield.VisualStudio.slnx`
- Unity package: test in the Unity Editor with sample serialized fields/assets

For rename behavior, test both positive and negative cases:

- A `[SerializeField]` field rename should add `[FormerlySerializedAs("oldName")]`.
- Non-serialized fields should not be modified.
- Static, const, and ambiguous multi-field declarations should be skipped.
- Duplicate `[FormerlySerializedAs]` attributes should not be added.

## Release Notes

Maintainers should update the relevant changelog or publishing notes for user-facing changes before creating a release.
