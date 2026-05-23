import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';

import { buildFormerlySerializedAsEdits, findRenamedSerializedFields, SerializedFieldRename } from './formerlySerializedAs';

// ─── Diagnostic logging ────────────────────────────────────────────────────────

function writeDiagnostic(message: string) {
	try {
		const logDir = path.join(os.homedir(), 'UnitySerializedShield');
		if (!fs.existsSync(logDir)) {
			fs.mkdirSync(logDir, { recursive: true });
		}
		const logPath = path.join(logDir, 'VSCodeExtension.log');
		const timestamp = new Date().toISOString();
		fs.appendFileSync(logPath, `${timestamp} ${message}\n`);
	} catch (e) {
		// Ignore log failures
	}
}

// ─── State ─────────────────────────────────────────────────────────────────────

// Snapshot taken the moment a rename starts (before the document is changed).
// Key: document URI string, Value: document text before rename.
const preRenameSnapshots = new Map<string, string>();

// Guards against reacting to our own programmatic insertions.
const documentsBeingUpdated = new Set<string>();

// Prevents infinite recursion in the RenameProvider.
let isDelegatingRename = false;

// ─── Activation ────────────────────────────────────────────────────────────────

export function activate(context: vscode.ExtensionContext) {
	writeDiagnostic('activate called');

	context.subscriptions.push(
		// Register our rename provider (used when no other higher-priority rename provider handles the rename).
		vscode.languages.registerRenameProvider(
			{ language: 'csharp', scheme: 'file' },
			new SerializedFieldRenameProvider(),
		),

		// Capture a pre-rename snapshot when the user invokes any rename command.
		// This is the key: we snapshot BEFORE the rename dialog opens.
		vscode.commands.registerCommand('UnitySerializedShield.captureAndRename', async () => {
			const editor = vscode.window.activeTextEditor;
			if (editor && isCsharpDocument(editor.document)) {
				captureSnapshot(editor.document);
			}
			await vscode.commands.executeCommand('editor.action.rename');
		}),


		// Passive fallback: detect renames applied by C# Dev Kit or OmniSharp.
		// We use the pre-rename snapshot to compute the FormerlySerializedAs insertions.
		vscode.workspace.onDidChangeTextDocument((event) => {
			void handleDocumentChange(event);
		}),

		// Focus-based snapshot capture
		vscode.window.onDidChangeActiveTextEditor((editor) => {
			if (editor && isCsharpDocument(editor.document)) {
				captureSnapshot(editor.document);
			}
		}),

		// Clean up snapshot on close.
		vscode.workspace.onDidCloseTextDocument((document) => {
			preRenameSnapshots.delete(document.uri.toString());
		}),

		// Update snapshot when document is saved (clean disk state).
		vscode.workspace.onDidSaveTextDocument((document) => {
			if (isCsharpDocument(document)) {
				preRenameSnapshots.set(document.uri.toString(), document.getText());
				writeDiagnostic(`onDidSaveTextDocument: updated snapshot for ${document.uri.toString()}`);
			}
		}),

		vscode.commands.registerCommand('UnitySerializedShield.renameSerializedField', () =>
			vscode.commands.executeCommand('UnitySerializedShield.captureAndRename')),

		vscode.commands.registerCommand('UnitySerializedShield.showStatus', () => {
			vscode.window.showInformationMessage('UnitySerializedShield is watching Unity serialized field renames.');
		}),
	);

	// Pre-populate snapshots for already-open and visible documents.
	for (const document of vscode.workspace.textDocuments) {
		if (isCsharpDocument(document)) {
			preRenameSnapshots.set(document.uri.toString(), document.getText());
		}
	}
	for (const editor of vscode.window.visibleTextEditors) {
		if (isCsharpDocument(editor.document)) {
			captureSnapshot(editor.document);
		}
	}

	// Capture snapshots when newly opened.
	context.subscriptions.push(
		vscode.workspace.onDidOpenTextDocument((document) => {
			if (isCsharpDocument(document)) {
				const key = document.uri.toString();
				// Only capture if clean (not dirty) to avoid clobbering a baseline during a rename.
				if (!document.isDirty || !preRenameSnapshots.has(key)) {
					preRenameSnapshots.set(key, document.getText());
					writeDiagnostic(`onDidOpenTextDocument: captured snapshot for ${key}`);
				}
			}
		}),
	);
}

export function deactivate() {
	writeDiagnostic('deactivate called');
}

// ─── Snapshot helper ───────────────────────────────────────────────────────────

function captureSnapshot(document: vscode.TextDocument) {
	const key = document.uri.toString();
	const text = document.getText();
	preRenameSnapshots.set(key, text);
	writeDiagnostic(`captureSnapshot: captured ${key}, length=${text.length}`);
}

// ─── Passive fallback handler ──────────────────────────────────────────────────

async function handleDocumentChange(event: vscode.TextDocumentChangeEvent) {
	const document = event.document;
	if (!isCsharpDocument(document) || event.contentChanges.length === 0) {
		return;
	}

	const documentKey = document.uri.toString();

	if (documentsBeingUpdated.has(documentKey)) {
		writeDiagnostic(`handleDocumentChange: skipping own edit for ${documentKey}`);
		// Update snapshot to match our own edit
		preRenameSnapshots.set(documentKey, document.getText());
		return;
	}

	let baselineText = preRenameSnapshots.get(documentKey);
	const currentText = document.getText();

	if (baselineText === undefined) {
		// Recovery Mechanism: If snapshot is missing (e.g. background document load during rename),
		// read the baseline from disk since the unsaved dirty memory buffer has the renamed text.
		if (document.uri.scheme === 'file' && fs.existsSync(document.uri.fsPath)) {
			try {
				const diskText = fs.readFileSync(document.uri.fsPath, 'utf8');
				if (diskText !== currentText) {
					const diskRenames = findRenamedSerializedFields(diskText, currentText);
					if (diskRenames.length > 0) {
						baselineText = diskText;
						preRenameSnapshots.set(documentKey, diskText);
						writeDiagnostic(`handleDocumentChange: recovered baseline from disk for ${documentKey}`);
					}
				}
			} catch (e) {
				writeDiagnostic(`handleDocumentChange: failed to read disk fallback: ${String(e)}`);
			}
		}

		if (baselineText === undefined) {
			preRenameSnapshots.set(documentKey, currentText);
			return;
		}
	}

	if (baselineText === currentText) {
		return;
	}

	const renames = findRenamedSerializedFields(baselineText, currentText);
	writeDiagnostic(`handleDocumentChange: found ${renames.length} renames: ${JSON.stringify(renames.map(r => `${r.previousName}->${r.currentName}`))}`);

	if (renames.length === 0) {
		// Not a serialized rename — update the snapshot.
		preRenameSnapshots.set(documentKey, currentText);
		return;
	}

	// Determine if this is a real rename command (not just typing).
	const isRenameEdit = isValidRenameEdit(event.contentChanges, baselineText, renames);
	writeDiagnostic(`handleDocumentChange: isRenameEdit=${isRenameEdit}, changes=${event.contentChanges.length}`);

	if (!isRenameEdit) {
		// Could be incremental typing — don't shift the baseline yet.
		// The user is still typing; we'll get more events.
		return;
	}

	// This is a confirmed rename. Build insertions and apply them.
	const insertions = buildFormerlySerializedAsEdits(baselineText, currentText);
	writeDiagnostic(`handleDocumentChange: built ${insertions.length} insertions`);

	if (insertions.length === 0) {
		// Nothing to add (e.g., FormerlySerializedAs already present).
		preRenameSnapshots.set(documentKey, currentText);
		return;
	}

	const workspaceEdit = new vscode.WorkspaceEdit();
	for (const insertion of insertions) {
		workspaceEdit.insert(document.uri, document.positionAt(insertion.offset), insertion.text);
	}

	documentsBeingUpdated.add(documentKey);
	try {
		writeDiagnostic(`handleDocumentChange: applying ${insertions.length} insertion(s)`);
		const success = await vscode.workspace.applyEdit(workspaceEdit);
		writeDiagnostic(`handleDocumentChange: applyEdit success=${success}`);
		preRenameSnapshots.set(documentKey, document.getText());
	} finally {
		documentsBeingUpdated.delete(documentKey);
	}
}

// ─── Rename detection ──────────────────────────────────────────────────────────

/**
 * Returns true if any content change is a valid identifier replacement
 * matching one of the expected renames, OR if the change is a bulk
 * (multi-character) replace that looks like a whole-word rename.
 * Ignores formatting-only changes (whitespace, semicolons, etc.).
 */
function isValidRenameEdit(
	contentChanges: readonly vscode.TextDocumentContentChangeEvent[],
	previousText: string,
	renames: readonly SerializedFieldRename[],
): boolean {
	// Multiple changes in one transaction usually means a language-server rename.
	if (contentChanges.length > 1) {
		writeDiagnostic(`isValidRenameEdit: multiple changes (${contentChanges.length}) — treating as rename`);
		return true;
	}

	const expectedRenames = new Set(renames.map((r) => `${r.previousName}\u0000${r.currentName}`));

	for (const change of contentChanges) {
		const replacedText = previousText.slice(change.rangeOffset, change.rangeOffset + change.rangeLength);
		const newText = change.text;

		writeDiagnostic(`isValidRenameEdit: replacedText="${replacedText}" (isId=${isIdentifier(replacedText)}), newText="${newText}" (isId=${isIdentifier(newText)}), expectedRenames=${JSON.stringify([...expectedRenames])}`);

		// Skip formatting changes.
		if (!isIdentifier(replacedText) || !isIdentifier(newText)) {
			continue;
		}

		// Exact match: the replaced text and new text match an expected rename.
		if (expectedRenames.has(`${replacedText}\u0000${newText}`)) {
			writeDiagnostic(`isValidRenameEdit: matched identifier change "${replacedText}" -> "${newText}"`);
			return true;
		}

		// Bulk replace heuristic: replaced span is longer than 2 chars on either side.
		if (replacedText.length > 2 || newText.length > 2) {
			writeDiagnostic(`isValidRenameEdit: bulk identifier change "${replacedText}" -> "${newText}"`);
			return true;
		}
	}

	return false;
}

// ─── RenameProvider (direct path, called when our provider wins) ───────────────

class SerializedFieldRenameProvider implements vscode.RenameProvider {
	prepareRename(document: vscode.TextDocument, position: vscode.Position) {
		// Capture a fresh snapshot just before rename starts.
		captureSnapshot(document);
		return document.getWordRangeAtPosition(position, /@?[A-Za-z_]\w*/);
	}

	async provideRenameEdits(
		document: vscode.TextDocument,
		position: vscode.Position,
		newName: string,
		token: vscode.CancellationToken,
	) {
		if (isDelegatingRename || token.isCancellationRequested) {
			return undefined;
		}

		const previousText = document.getText();
		let renameEdit: vscode.WorkspaceEdit | undefined;

		isDelegatingRename = true;
		try {
			renameEdit = await vscode.commands.executeCommand<vscode.WorkspaceEdit | undefined>(
				'vscode.executeDocumentRenameProvider',
				document.uri,
				position,
				newName,
			);
		} finally {
			isDelegatingRename = false;
		}

		if (token.isCancellationRequested) {
			return undefined;
		}

		renameEdit ??= buildLocalRenameEdit(document, position, newName);

		if (!renameEdit) {
			return undefined;
		}

		writeDiagnostic(`provideRenameEdits: got renameEdit, building FormerlySerializedAs`);

		const renamedText = applyWorkspaceEditToDocumentText(document, previousText, renameEdit);
		const insertions = buildFormerlySerializedAsEdits(previousText, renamedText);

		writeDiagnostic(`provideRenameEdits: built ${insertions.length} insertions`);

		for (const insertion of insertions) {
			renameEdit.insert(document.uri, document.positionAt(insertion.offset), insertion.text);
		}

		return renameEdit;
	}
}

// ─── Helpers ───────────────────────────────────────────────────────────────────

function buildLocalRenameEdit(document: vscode.TextDocument, position: vscode.Position, newName: string) {
	const wordRange = document.getWordRangeAtPosition(position, /@?[A-Za-z_]\w*/);

	if (!wordRange) {
		return undefined;
	}

	const edit = new vscode.WorkspaceEdit();
	edit.replace(document.uri, wordRange, newName);

	return edit;
}

function applyWorkspaceEditToDocumentText(
	document: vscode.TextDocument,
	text: string,
	workspaceEdit: vscode.WorkspaceEdit,
) {
	const documentEdits = workspaceEdit
		.entries()
		.filter(([uri]) => uri.toString() === document.uri.toString())
		.flatMap(([, edits]) => edits)
		.sort((left, right) => document.offsetAt(right.range.start) - document.offsetAt(left.range.start));

	return documentEdits.reduce((updatedText, edit) => {
		const startOffset = document.offsetAt(edit.range.start);
		const endOffset = document.offsetAt(edit.range.end);

		return `${updatedText.slice(0, startOffset)}${edit.newText}${updatedText.slice(endOffset)}`;
	}, text);
}

function isCsharpDocument(document: vscode.TextDocument) {
	return document.languageId === 'csharp' || document.uri.fsPath.toLowerCase().endsWith('.cs');
}

function isIdentifier(text: string) {
	return /^@?[A-Za-z_]\w*$/.test(text);
}
