# Contributing to UnitySerializedShield

Thanks for helping improve UnitySerializedShield. This project is a Visual Studio Code extension for Unity C# projects, so changes should stay careful around source edits and Unity serialization behavior.

## Setup

Install dependencies:

```powershell
npm install
```

Compile:

```powershell
npm run compile
```

Run linting and tests:

```powershell
npm test
```

## Local Extension Testing

1. Open this repository in Visual Studio Code.
2. Press `F5`.
3. In the Extension Development Host window, open or create a Unity `.cs` file.
4. Rename a field marked with `[SerializeField]`.
5. Confirm that `[FormerlySerializedAs("oldName")]` is inserted only when the rename is safe to detect.

## Pull Request Guidelines

- Keep pull requests focused and small when possible.
- Add tests for parser or rename behavior changes.
- Preserve safe skip behavior when a rename is ambiguous.
- Do not include generated `.vsix` files in pull requests.
- Run `npm test` before opening the pull request.
- Update `README.md` or `DEVELOPMENT.md` when behavior or workflow changes.

## Useful Files

- `src/extension.ts`: VS Code activation and document-change handling.
- `src/formerlySerializedAs.ts`: rename comparison and edit creation.
- `src/serializedFieldParser.ts`: C# field parsing.
- `src/textUtils.ts`: shared text helpers.
- `src/test/extension.test.ts`: automated behavior tests.

## Release Notes

Maintainers should update `CHANGELOG.md` for user-facing changes before creating a release.
