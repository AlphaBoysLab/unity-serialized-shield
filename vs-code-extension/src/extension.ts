import * as vscode from 'vscode';

import { buildFormerlySerializedAsEdits, findRenamedSerializedFields } from './formerlySerializedAs';

const documentSnapshots = new Map<string, string>();
const documentsBeingUpdated = new Set<string>();

export function activate(context: vscode.ExtensionContext) {
	for (const document of vscode.workspace.textDocuments) {
		rememberDocument(document);
	}

	context.subscriptions.push(
		vscode.workspace.onDidOpenTextDocument(rememberDocument),
		vscode.workspace.onDidCloseTextDocument((document) => documentSnapshots.delete(document.uri.toString())),
		vscode.workspace.onDidChangeTextDocument((event) => void protectSerializedFieldRenames(event)),
		vscode.commands.registerCommand('UnitySerializedShield.showStatus', () => {
			vscode.window.showInformationMessage('UnitySerializedShield is watching Unity serialized field renames.');
		}),
	);
}

export function deactivate() {}

async function protectSerializedFieldRenames(event: vscode.TextDocumentChangeEvent) {
	const document = event.document;

	if (!isCsharpDocument(document)) {
		return;
	}

	const documentKey = document.uri.toString();
	const currentText = document.getText();

	if (documentsBeingUpdated.has(documentKey)) {
		documentSnapshots.set(documentKey, currentText);
		return;
	}

	const previousText = documentSnapshots.get(documentKey);
	documentSnapshots.set(documentKey, currentText);

	if (previousText === undefined || previousText === currentText) {
		return;
	}

	if (!isRenameCommandEdit(event, previousText, currentText)) {
		return;
	}

	const insertions = buildFormerlySerializedAsEdits(previousText, currentText);

	if (insertions.length === 0) {
		return;
	}

	const workspaceEdit = new vscode.WorkspaceEdit();

	for (const insertion of insertions) {
		workspaceEdit.insert(document.uri, document.positionAt(insertion.offset), insertion.text);
	}

	documentsBeingUpdated.add(documentKey);

	try {
		await vscode.workspace.applyEdit(workspaceEdit);
		documentSnapshots.set(documentKey, document.getText());
	} finally {
		documentsBeingUpdated.delete(documentKey);
	}
}

function isRenameCommandEdit(event: vscode.TextDocumentChangeEvent, previousText: string, currentText: string) {
	const renames = findRenamedSerializedFields(previousText, currentText);

	if (renames.length === 0 || event.contentChanges.length === 0) {
		return false;
	}

	const expectedRenames = new Set(renames.map((rename) => `${rename.previousName}\u0000${rename.currentName}`));
	let changedSerializedFieldName = false;

	for (const change of event.contentChanges) {
		const previousName = previousText.slice(change.rangeOffset, change.rangeOffset + change.rangeLength);
		const currentName = change.text;

		if (!isIdentifier(previousName) || !isIdentifier(currentName)) {
			return false;
		}

		if (expectedRenames.has(`${previousName}\u0000${currentName}`)) {
			changedSerializedFieldName = true;
		}
	}

	return changedSerializedFieldName;
}

function isIdentifier(text: string) {
	return /^@?[A-Za-z_]\w*$/.test(text);
}

function rememberDocument(document: vscode.TextDocument) {
	if (isCsharpDocument(document)) {
		documentSnapshots.set(document.uri.toString(), document.getText());
	}
}

function isCsharpDocument(document: vscode.TextDocument) {
	return document.languageId === 'csharp' || document.uri.fsPath.toLowerCase().endsWith('.cs');
}
