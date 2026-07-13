// Pure decision logic used by extension.ts. No VS Code API dependency so the
// event/offset machinery is unit-testable.

// ─── Replacement spans and offset mapping ──────────────────────────────────────

// A text replacement expressed in PRE-edit document offsets.
export type ReplacementSpan = {
	startOffset: number;
	endOffset: number;
	newText: string;
};

export function sortReplacementSpans(spans: ReplacementSpan[]): ReplacementSpan[] {
	return [...spans].sort((left, right) => left.startOffset - right.startOffset);
}

// Applies non-overlapping replacement spans (pre-edit offsets) to the text.
export function applyReplacementSpans(text: string, spans: ReplacementSpan[]): string {
	let result = '';
	let cursor = 0;

	for (const span of sortReplacementSpans(spans)) {
		result += text.slice(cursor, span.startOffset) + span.newText;
		cursor = span.endOffset;
	}

	return result + text.slice(cursor);
}

// Maps an offset measured in the POST-edit text back to the equivalent offset
// in the PRE-edit text, given the replacement spans that produced it. Needed
// because FormerlySerializedAs insertion offsets are computed against the
// renamed text but must be applied to the original document in the same
// WorkspaceEdit (audit W-C4). An offset that falls inside replaced text is
// clamped to the start of that replacement.
export function mapPostEditOffsetToPreEditOffset(postOffset: number, spans: ReplacementSpan[]): number {
	let delta = 0;

	for (const span of sortReplacementSpans(spans)) {
		const postStart = span.startOffset + delta;
		const postEnd = postStart + span.newText.length;

		if (postOffset < postStart) {
			break;
		}

		if (postOffset >= postEnd) {
			delta += span.newText.length - (span.endOffset - span.startOffset);
			continue;
		}

		return span.startOffset;
	}

	return postOffset - delta;
}

// ─── Document change classification ────────────────────────────────────────────

// Mirrors vscode.TextDocumentChangeReason without importing the VS Code API.
export const textDocumentChangeReasonUndo = 1;
export const textDocumentChangeReasonRedo = 2;

export type DocumentChangeDecision =
	| 'skip-empty'
	| 'skip-own-edit'
	| 'skip-undo-redo'
	| 'skip-multi-change'
	| 'process';

export type DocumentChangeInfo = {
	contentChangeCount: number;
	reason: number | undefined;
	isOwnEdit: boolean;
};

// Decides how a text document change event must be handled:
// - our own programmatic edits are never re-processed (guards recursion);
// - undo/redo is never treated as a rename (audit W-C3) — undoing a rename
//   must not insert a reverse FormerlySerializedAs attribute;
// - multi-change transactions are never treated as a rename (audit W-C1) —
//   multi-cursor typing produces exactly this shape and must never insert
//   attributes.
export function classifyDocumentChange(change: DocumentChangeInfo): DocumentChangeDecision {
	if (change.contentChangeCount === 0) {
		return 'skip-empty';
	}

	if (change.isOwnEdit) {
		return 'skip-own-edit';
	}

	if (change.reason === textDocumentChangeReasonUndo || change.reason === textDocumentChangeReasonRedo) {
		return 'skip-undo-redo';
	}

	if (change.contentChangeCount > 1) {
		return 'skip-multi-change';
	}

	return 'process';
}
