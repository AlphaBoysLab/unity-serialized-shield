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

// Holds active debounce timeouts for change detection.
const pendingDebounceTimeouts = new Map<string, NodeJS.Timeout>();

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

		// Clean up snapshot and timeout on close.
		vscode.workspace.onDidCloseTextDocument((document) => {
			const key = document.uri.toString();
			preRenameSnapshots.delete(key);
			const timeout = pendingDebounceTimeouts.get(key);
			if (timeout) {
				clearTimeout(timeout);
				pendingDebounceTimeouts.delete(key);
			}
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

	// If there are multiple changes in one transaction, it's highly likely to be a language-server rename.
	// We can process it instantly without debouncing!
	if (event.contentChanges.length > 1) {
		writeDiagnostic(`handleDocumentChange: instant processing for multi-change edit (${event.contentChanges.length})`);
		const existingTimeout = pendingDebounceTimeouts.get(documentKey);
		if (existingTimeout) {
			clearTimeout(existingTimeout);
			pendingDebounceTimeouts.delete(documentKey);
		}
		await processDocumentChange(document);
		return;
	}

	// For single changes (which could be incremental typing or optimized minimal-diff F2 renames),
	// debounce for 300ms to allow typing to settle before processing.
	const existingTimeout = pendingDebounceTimeouts.get(documentKey);
	if (existingTimeout) {
		clearTimeout(existingTimeout);
	}

	const timeout = setTimeout(async () => {
		pendingDebounceTimeouts.delete(documentKey);
		await processDocumentChange(document);
	}, 300); // 300ms sweet-spot debounce

	pendingDebounceTimeouts.set(documentKey, timeout);
}

async function processDocumentChange(document: vscode.TextDocument) {
	const documentKey = document.uri.toString();
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
						writeDiagnostic(`processDocumentChange: recovered baseline from disk for ${documentKey}`);
					}
				}
			} catch (e) {
				writeDiagnostic(`processDocumentChange: failed to read disk fallback: ${String(e)}`);
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
	writeDiagnostic(`processDocumentChange: found ${renames.length} renames: ${JSON.stringify(renames.map(r => `${r.previousName}->${r.currentName}`))}`);

	if (renames.length === 0) {
		// Not a serialized rename — update the snapshot.
		preRenameSnapshots.set(documentKey, currentText);
		return;
	}

	// Build insertions and apply them.
	const insertions = buildFormerlySerializedAsEdits(baselineText, currentText);
	writeDiagnostic(`processDocumentChange: built ${insertions.length} insertions`);

	if (insertions.length === 0) {
		preRenameSnapshots.set(documentKey, currentText);
		return;
	}

	const workspaceEdit = new vscode.WorkspaceEdit();
	for (const insertion of insertions) {
		workspaceEdit.insert(document.uri, document.positionAt(insertion.offset), insertion.text);
	}

	documentsBeingUpdated.add(documentKey);
	try {
		writeDiagnostic(`processDocumentChange: applying ${insertions.length} insertion(s)`);
		const success = await vscode.workspace.applyEdit(workspaceEdit);
		writeDiagnostic(`processDocumentChange: applyEdit success=${success}`);
		preRenameSnapshots.set(documentKey, document.getText());
	} finally {
		documentsBeingUpdated.delete(documentKey);
	}
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
