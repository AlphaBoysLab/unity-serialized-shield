import * as vscode from 'vscode';

import { buildFormerlySerializedAsEdits } from './formerlySerializedAs';

const documentSnapshots = new Map<string, string>();
const documentsBeingUpdated = new Set<string>();
let isDelegatingRename = false;

export function activate(context: vscode.ExtensionContext) {
	for (const document of vscode.workspace.textDocuments) {
		rememberDocument(document);
	}

	context.subscriptions.push(
		vscode.workspace.onDidOpenTextDocument(rememberDocument),
		vscode.workspace.onDidCloseTextDocument((document) => documentSnapshots.delete(document.uri.toString())),
		vscode.workspace.onDidChangeTextDocument((event) => rememberChangedDocument(event.document)),
		vscode.languages.registerRenameProvider(
			{ language: 'csharp', scheme: 'file' },
			new SerializedFieldRenameProvider(),
		),
		vscode.commands.registerCommand('UnitySerializedShield.renameSerializedField', () =>
			vscode.commands.executeCommand('editor.action.rename')),
		vscode.commands.registerCommand('UnitySerializedShield.showStatus', () => {
			vscode.window.showInformationMessage('UnitySerializedShield is watching Unity serialized field renames.');
		}),
	);
}

export function deactivate() {}

function rememberChangedDocument(document: vscode.TextDocument) {
	if (!isCsharpDocument(document)) {
		return;
	}

	const documentKey = document.uri.toString();

	if (!documentsBeingUpdated.has(documentKey)) {
		documentSnapshots.set(documentKey, document.getText());
	}
}

function rememberDocument(document: vscode.TextDocument) {
	if (isCsharpDocument(document)) {
		documentSnapshots.set(document.uri.toString(), document.getText());
	}
}

function isCsharpDocument(document: vscode.TextDocument) {
	return document.languageId === 'csharp' || document.uri.fsPath.toLowerCase().endsWith('.cs');
}

class SerializedFieldRenameProvider implements vscode.RenameProvider {
	prepareRename(document: vscode.TextDocument, position: vscode.Position) {
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

		const renamedText = applyWorkspaceEditToDocumentText(document, previousText, renameEdit);
		const insertions = buildFormerlySerializedAsEdits(previousText, renamedText);

		for (const insertion of insertions) {
			renameEdit.insert(document.uri, document.positionAt(insertion.offset), insertion.text);
		}

		return renameEdit;
	}
}

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
