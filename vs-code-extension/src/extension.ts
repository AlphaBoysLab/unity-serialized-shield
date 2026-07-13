import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';

import { buildFormerlySerializedAsEdits, findSafePassiveRename, TextInsertion } from './formerlySerializedAs';
import { parseSerializedFields } from './serializedFieldParser';
import {
	applyReplacementSpans,
	classifyDocumentChange,
	mapPostEditOffsetToPreEditOffset,
	ReplacementSpan,
	sortReplacementSpans,
} from './renameLogic';
import { countIdentifierOccurrencesInCode } from './textUtils';

const identifierPattern = /@?[A-Za-z_]\w*/;

// ─── Configuration ─────────────────────────────────────────────────────────────

type ExtensionConfig = {
	enabled: boolean;
	passiveEnabled: boolean;
	passiveDebounceMs: number;
	loggingEnabled: boolean;
	statusBarNotification: boolean;
};

function getConfig(): ExtensionConfig {
	const configuration = vscode.workspace.getConfiguration('unitySerializedShield');

	return {
		enabled: configuration.get<boolean>('enabled', true),
		passiveEnabled: configuration.get<boolean>('enablePassiveRenameDetection', false),
		passiveDebounceMs: configuration.get<number>('passiveDebounceMs', 1500),
		loggingEnabled: configuration.get<boolean>('enableLogging', false),
		statusBarNotification: configuration.get<boolean>('showStatusBarNotification', true),
	};
}

// ─── Diagnostic logging (opt-in, bounded, async) ───────────────────────────────

const maxLogBytes = 512 * 1024;
let logWriteChain: Promise<void> = Promise.resolve();

function writeDiagnostic(message: string) {
	if (!getConfig().loggingEnabled) {
		return;
	}

	const line = `${new Date().toISOString()} ${message}\n`;

	logWriteChain = logWriteChain.then(async () => {
		try {
			const logDir = path.join(os.homedir(), 'UnitySerializedShield');
			await fs.promises.mkdir(logDir, { recursive: true });
			const logPath = path.join(logDir, 'VSCodeExtension.log');

			try {
				const stats = await fs.promises.stat(logPath);

				if (stats.size > maxLogBytes) {
					const rotatedPath = `${logPath}.old`;
					await fs.promises.rm(rotatedPath, { force: true });
					await fs.promises.rename(logPath, rotatedPath);
				}
			} catch {
				// Log file does not exist yet.
			}

			await fs.promises.appendFile(logPath, line);
		} catch {
			// Ignore log failures.
		}
	});
}

// ─── State ─────────────────────────────────────────────────────────────────────

// Snapshot taken before a rename changes the document.
// Key: document URI string, Value: document text before rename.
const preRenameSnapshots = new Map<string, string>();

// Guards against reacting to our own programmatic insertions.
const documentsBeingUpdated = new Set<string>();

// Holds active debounce timeouts for passive change detection.
const pendingDebounceTimeouts = new Map<string, NodeJS.Timeout>();

// Prevents infinite recursion in the RenameProvider, per document URI.
const delegatingRenameUris = new Set<string>();

// ─── Activation ────────────────────────────────────────────────────────────────

export function activate(context: vscode.ExtensionContext) {
	writeDiagnostic('activate called');

	context.subscriptions.push(
		// Register our rename provider (used when no other higher-priority rename provider handles the rename).
		vscode.languages.registerRenameProvider(
			{ language: 'csharp', scheme: 'file' },
			new SerializedFieldRenameProvider(),
		),

		// Capture a pre-rename snapshot, then run the native rename command.
		vscode.commands.registerCommand('UnitySerializedShield.captureAndRename', async () => {
			try {
				const editor = vscode.window.activeTextEditor;
				if (editor && isWatchedDocument(editor.document)) {
					captureSnapshot(editor.document);
				}
				await vscode.commands.executeCommand('editor.action.rename');
			} catch (error) {
				writeDiagnostic(`captureAndRename error: ${String(error)}`);
			}
		}),

		// Deterministic protected rename: asks for the new name, delegates the
		// rename to the installed C# rename provider, and applies the rename plus
		// the [FormerlySerializedAs] insertions as one workspace edit.
		vscode.commands.registerCommand('UnitySerializedShield.renameSerializedField', async () => {
			try {
				await runProtectedRenameCommand();
			} catch (error) {
				writeDiagnostic(`renameSerializedField error: ${String(error)}`);
			}
		}),

		vscode.commands.registerCommand('UnitySerializedShield.showStatus', () => {
			const config = getConfig();
			const parts = [
				config.enabled ? 'UnitySerializedShield is active.' : 'UnitySerializedShield is disabled by the unitySerializedShield.enabled setting.',
				`Passive rename detection: ${config.passiveEnabled ? 'on (experimental)' : 'off'}.`,
			];
			void vscode.window.showInformationMessage(parts.join(' '));
		}),

		// Passive fallback (opt-in): detect renames applied by other providers by
		// diffing document text against the pre-rename snapshot.
		vscode.workspace.onDidChangeTextDocument((event) => {
			try {
				handleDocumentChange(event);
			} catch (error) {
				writeDiagnostic(`handleDocumentChange error: ${String(error)}`);
			}
		}),

		// Focus-based snapshot capture.
		vscode.window.onDidChangeActiveTextEditor((editor) => {
			if (editor && isWatchedDocument(editor.document)) {
				captureSnapshot(editor.document);
			}
		}),

		// Clean up snapshot and timeout on close.
		vscode.workspace.onDidCloseTextDocument((document) => {
			const key = document.uri.toString();
			preRenameSnapshots.delete(key);
			cancelPendingDebounce(key);
		}),

		// Update snapshot when document is saved (clean disk state).
		vscode.workspace.onDidSaveTextDocument((document) => {
			if (isWatchedDocument(document)) {
				preRenameSnapshots.set(document.uri.toString(), document.getText());
			}
		}),

		// Capture snapshots when newly opened.
		vscode.workspace.onDidOpenTextDocument((document) => {
			if (isWatchedDocument(document)) {
				const key = document.uri.toString();
				// Only capture if clean (not dirty) to avoid clobbering a baseline during a rename.
				if (!document.isDirty || !preRenameSnapshots.has(key)) {
					preRenameSnapshots.set(key, document.getText());
				}
			}
		}),
	);

	// Pre-populate snapshots for already-open and visible documents.
	for (const document of vscode.workspace.textDocuments) {
		if (isWatchedDocument(document)) {
			preRenameSnapshots.set(document.uri.toString(), document.getText());
		}
	}
	for (const editor of vscode.window.visibleTextEditors) {
		if (isWatchedDocument(editor.document)) {
			captureSnapshot(editor.document);
		}
	}
}

export function deactivate() {
	for (const timeout of pendingDebounceTimeouts.values()) {
		clearTimeout(timeout);
	}
	pendingDebounceTimeouts.clear();
	writeDiagnostic('deactivate called');
}

// ─── Snapshot helpers ──────────────────────────────────────────────────────────

function captureSnapshot(document: vscode.TextDocument) {
	preRenameSnapshots.set(document.uri.toString(), document.getText());
}

function cancelPendingDebounce(documentKey: string) {
	const timeout = pendingDebounceTimeouts.get(documentKey);

	if (timeout) {
		clearTimeout(timeout);
		pendingDebounceTimeouts.delete(documentKey);
	}
}

// ─── Passive fallback handler ──────────────────────────────────────────────────

function handleDocumentChange(event: vscode.TextDocumentChangeEvent) {
	const document = event.document;

	if (!isWatchedDocument(document)) {
		return;
	}

	const documentKey = document.uri.toString();
	const decision = classifyDocumentChange({
		contentChangeCount: event.contentChanges.length,
		reason: event.reason,
		isOwnEdit: documentsBeingUpdated.has(documentKey),
	});

	switch (decision) {
		case 'skip-empty':
			return;

		case 'skip-own-edit':
			// Do not touch the baseline here: a user edit racing our own applyEdit
			// must not be silently absorbed (audit W-C12). The apply routine
			// reconciles the baseline itself once the edit resolves.
			return;

		case 'skip-undo-redo':
		case 'skip-multi-change':
			// Never treated as a rename (audit W-C1/W-C3), but the baseline must
			// follow the document so later diffs stay consistent.
			cancelPendingDebounce(documentKey);
			preRenameSnapshots.set(documentKey, document.getText());
			return;

		case 'process':
			break;
	}

	const config = getConfig();

	if (!config.enabled || !config.passiveEnabled) {
		// Passive detection is off: keep the baseline in sync so turning it on
		// later starts from a clean state.
		cancelPendingDebounce(documentKey);
		preRenameSnapshots.set(documentKey, document.getText());
		return;
	}

	schedulePassiveProcessing(documentKey, document, config.passiveDebounceMs);
}

function schedulePassiveProcessing(documentKey: string, document: vscode.TextDocument, debounceMs: number) {
	cancelPendingDebounce(documentKey);

	const timeout = setTimeout(() => {
		pendingDebounceTimeouts.delete(documentKey);
		processPassiveChange(document).catch((error) => {
			writeDiagnostic(`processPassiveChange error: ${String(error)}`);
		});
	}, debounceMs);

	pendingDebounceTimeouts.set(documentKey, timeout);
}

async function processPassiveChange(document: vscode.TextDocument) {
	const documentKey = document.uri.toString();
	let baselineText = preRenameSnapshots.get(documentKey);
	const currentText = document.getText();
	const versionAtRead = document.version;

	if (baselineText === undefined) {
		// Recovery mechanism: if the snapshot is missing (e.g. background document
		// load during a rename), read the baseline from disk since the unsaved
		// dirty memory buffer has the renamed text.
		if (document.uri.scheme === 'file' && fs.existsSync(document.uri.fsPath)) {
			try {
				const diskText = fs.readFileSync(document.uri.fsPath, 'utf8');

				if (diskText !== currentText && findSafePassiveRename(diskText, currentText)) {
					baselineText = diskText;
					preRenameSnapshots.set(documentKey, diskText);
					writeDiagnostic(`processPassiveChange: recovered baseline from disk for ${documentKey}`);
				}
			} catch (error) {
				writeDiagnostic(`processPassiveChange: failed to read disk fallback: ${String(error)}`);
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

	const rename = findSafePassiveRename(baselineText, currentText);

	if (!rename) {
		// Not an unambiguous serialized rename — advance the baseline.
		preRenameSnapshots.set(documentKey, currentText);
		return;
	}

	writeDiagnostic(`processPassiveChange: safe rename ${rename.previousName} -> ${rename.currentName}`);

	const insertions = buildFormerlySerializedAsEdits(baselineText, currentText);

	if (insertions.length === 0) {
		preRenameSnapshots.set(documentKey, currentText);
		return;
	}

	await applyInsertionsToDocument(document, insertions, versionAtRead);
}

// Applies insertions (offsets valid for the document text at expectedVersion).
// The baseline is only advanced when the edit actually applied cleanly:
// - applyEdit === false leaves the baseline untouched so the rename is not
//   permanently lost (audit W-C11);
// - a user edit racing our applyEdit triggers a re-evaluation against the old
//   baseline instead of silently clobbering it (audit W-C12).
async function applyInsertionsToDocument(
	document: vscode.TextDocument,
	insertions: TextInsertion[],
	expectedVersion: number,
) {
	const documentKey = document.uri.toString();

	if (document.version !== expectedVersion) {
		writeDiagnostic('applyInsertionsToDocument: document changed before apply; rescheduling');
		schedulePassiveProcessing(documentKey, document, getConfig().passiveDebounceMs);
		return false;
	}

	const workspaceEdit = new vscode.WorkspaceEdit();

	for (const insertion of insertions) {
		workspaceEdit.insert(document.uri, document.positionAt(insertion.offset), insertion.text);
	}

	const versionBeforeApply = document.version;

	documentsBeingUpdated.add(documentKey);
	let success = false;
	try {
		success = await vscode.workspace.applyEdit(workspaceEdit);
	} finally {
		documentsBeingUpdated.delete(documentKey);
	}

	if (!success) {
		writeDiagnostic('applyInsertionsToDocument: applyEdit failed; baseline preserved');
		return false;
	}

	if (document.version === versionBeforeApply + 1) {
		preRenameSnapshots.set(documentKey, document.getText());
	} else {
		writeDiagnostic('applyInsertionsToDocument: user edit raced applyEdit; re-evaluating');
		schedulePassiveProcessing(documentKey, document, getConfig().passiveDebounceMs);
	}

	showInsertionNotice(insertions);
	return true;
}

function showInsertionNotice(insertions: TextInsertion[]) {
	if (!getConfig().statusBarNotification) {
		return;
	}

	const attributeCount = insertions.filter((insertion) => insertion.text.includes('FormerlySerializedAs')).length;

	if (attributeCount > 0) {
		vscode.window.setStatusBarMessage(
			`UnitySerializedShield: added [FormerlySerializedAs] for ${attributeCount} field${attributeCount === 1 ? '' : 's'}`,
			5000,
		);
	}
}

// ─── Deterministic protected rename command ────────────────────────────────────

async function runProtectedRenameCommand() {
	const config = getConfig();

	if (!config.enabled) {
		void vscode.window.showInformationMessage('UnitySerializedShield is disabled (unitySerializedShield.enabled).');
		return;
	}

	const editor = vscode.window.activeTextEditor;

	if (!editor || !isWatchedDocument(editor.document)) {
		return;
	}

	const document = editor.document;
	const wordRange = document.getWordRangeAtPosition(editor.selection.active, identifierPattern);

	if (!wordRange) {
		void vscode.window.showInformationMessage('Place the cursor on a serialized field name to rename it.');
		return;
	}

	const oldName = document.getText(wordRange);
	const previousText = document.getText();
	const fields = parseSerializedFields(previousText);

	if (!fields.some((field) => field.name === oldName)) {
		// Not a serialized field: hand over to the native rename untouched.
		await vscode.commands.executeCommand('editor.action.rename');
		return;
	}

	const newName = await vscode.window.showInputBox({
		prompt: `Rename serialized field '${oldName}'`,
		value: oldName,
		validateInput: (value) => (isIdentifier(value) ? undefined : 'Enter a valid C# identifier.'),
	});

	if (!newName || newName === oldName) {
		return;
	}

	const documentKey = document.uri.toString();
	const versionBeforeRename = document.version;
	let renameEdit = await executeDelegatedRename(document, wordRange.start, newName);

	if (document.version !== versionBeforeRename) {
		void vscode.window.showWarningMessage('UnitySerializedShield: the document changed during the rename; nothing was modified.');
		return;
	}

	renameEdit ??= buildLocalRenameEdit(document, wordRange.start, newName);

	if (!renameEdit) {
		void vscode.window.showWarningMessage(
			'UnitySerializedShield: no rename provider produced a safe rename for this field, so nothing was modified.',
		);
		return;
	}

	const insertions = addFormerlySerializedAsToRenameEdit(document, previousText, renameEdit);

	documentsBeingUpdated.add(documentKey);
	let success = false;
	try {
		success = await vscode.workspace.applyEdit(renameEdit);
	} finally {
		documentsBeingUpdated.delete(documentKey);
	}

	if (!success) {
		void vscode.window.showWarningMessage('UnitySerializedShield: the rename edit could not be applied.');
		return;
	}

	preRenameSnapshots.set(documentKey, document.getText());
	showInsertionNotice(insertions);
}

// ─── RenameProvider (direct path, called when our provider wins) ───────────────

class SerializedFieldRenameProvider implements vscode.RenameProvider {
	prepareRename(document: vscode.TextDocument, position: vscode.Position) {
		// Capture a fresh snapshot just before rename starts.
		captureSnapshot(document);
		return document.getWordRangeAtPosition(position, identifierPattern);
	}

	async provideRenameEdits(
		document: vscode.TextDocument,
		position: vscode.Position,
		newName: string,
		token: vscode.CancellationToken,
	) {
		try {
			return await this.buildRenameEdits(document, position, newName, token);
		} catch (error) {
			writeDiagnostic(`provideRenameEdits error: ${String(error)}`);
			return undefined;
		}
	}

	private async buildRenameEdits(
		document: vscode.TextDocument,
		position: vscode.Position,
		newName: string,
		token: vscode.CancellationToken,
	) {
		const documentKey = document.uri.toString();

		if (delegatingRenameUris.has(documentKey) || token.isCancellationRequested) {
			return undefined;
		}

		if (!getConfig().enabled) {
			return undefined;
		}

		const previousText = document.getText();
		let renameEdit = await executeDelegatedRename(document, position, newName);

		if (token.isCancellationRequested) {
			return undefined;
		}

		renameEdit ??= buildLocalRenameEdit(document, position, newName);

		if (!renameEdit) {
			return undefined;
		}

		addFormerlySerializedAsToRenameEdit(document, previousText, renameEdit);

		return renameEdit;
	}
}

// ─── Helpers ───────────────────────────────────────────────────────────────────

async function executeDelegatedRename(document: vscode.TextDocument, position: vscode.Position, newName: string) {
	const documentKey = document.uri.toString();

	delegatingRenameUris.add(documentKey);
	try {
		return await vscode.commands.executeCommand<vscode.WorkspaceEdit | undefined>(
			'vscode.executeDocumentRenameProvider',
			document.uri,
			position,
			newName,
		);
	} catch (error) {
		writeDiagnostic(`executeDelegatedRename: ${String(error)}`);
		return undefined;
	} finally {
		delegatingRenameUris.delete(documentKey);
	}
}

// Computes the [FormerlySerializedAs] insertions for the rename edit and adds
// them to it. Insertion offsets are computed against the post-rename text and
// then mapped back to pre-rename document coordinates before positionAt is
// used, so attributes are never spliced mid-line when references precede the
// declaration (audit W-C4).
function addFormerlySerializedAsToRenameEdit(
	document: vscode.TextDocument,
	previousText: string,
	renameEdit: vscode.WorkspaceEdit,
): TextInsertion[] {
	const renameSpans = collectDocumentReplacementSpans(document, renameEdit);
	const renamedText = applyReplacementSpans(previousText, renameSpans);
	const insertions = buildFormerlySerializedAsEdits(previousText, renamedText);

	for (const insertion of insertions) {
		const preEditOffset = mapPostEditOffsetToPreEditOffset(insertion.offset, renameSpans);
		renameEdit.insert(document.uri, document.positionAt(preEditOffset), insertion.text);
	}

	return insertions;
}

function collectDocumentReplacementSpans(document: vscode.TextDocument, workspaceEdit: vscode.WorkspaceEdit): ReplacementSpan[] {
	const spans = workspaceEdit
		.entries()
		.filter(([uri]) => uri.toString() === document.uri.toString())
		.flatMap(([, edits]) => edits)
		.map((edit) => ({
			startOffset: document.offsetAt(edit.range.start),
			endOffset: document.offsetAt(edit.range.end),
			newText: edit.newText,
		}));

	return sortReplacementSpans(spans);
}

// Local single-document fallback used only when no other rename provider
// produced an edit. It is deliberately conservative (audit W-C13): it only
// acts when the identifier is a known serialized field AND the old name occurs
// exactly once in code (the declaration itself), so replacing the occurrence
// under the cursor is a complete rename. Anything else returns undefined so no
// attribute is appended for a partial rename.
function buildLocalRenameEdit(document: vscode.TextDocument, position: vscode.Position, newName: string) {
	if (!isIdentifier(newName)) {
		return undefined;
	}

	const wordRange = document.getWordRangeAtPosition(position, identifierPattern);

	if (!wordRange) {
		return undefined;
	}

	const oldName = document.getText(wordRange);
	const text = document.getText();
	const fields = parseSerializedFields(text);

	if (!fields.some((field) => field.name === oldName)) {
		return undefined;
	}

	const serializedName = oldName.startsWith('@') ? oldName.slice(1) : oldName;

	if (countIdentifierOccurrencesInCode(text, serializedName) !== 1) {
		return undefined;
	}

	const edit = new vscode.WorkspaceEdit();
	edit.replace(document.uri, wordRange, newName);

	return edit;
}

function isWatchedDocument(document: vscode.TextDocument) {
	return (
		document.uri.scheme === 'file' &&
		(document.languageId === 'csharp' || document.uri.fsPath.toLowerCase().endsWith('.cs'))
	);
}

function isIdentifier(text: string) {
	return /^@?[A-Za-z_]\w*$/.test(text);
}
