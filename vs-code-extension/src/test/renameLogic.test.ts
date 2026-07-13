import * as assert from 'assert';

import {
	applyReplacementSpans,
	classifyDocumentChange,
	mapPostEditOffsetToPreEditOffset,
	ReplacementSpan,
	textDocumentChangeReasonRedo,
	textDocumentChangeReasonUndo,
} from '../renameLogic';
import { buildFormerlySerializedAsEdits, findSafePassiveRename } from '../formerlySerializedAs';

suite('classifyDocumentChange', () => {
	test('empty change events are skipped', () => {
		assert.strictEqual(
			classifyDocumentChange({ contentChangeCount: 0, reason: undefined, isOwnEdit: false }),
			'skip-empty',
		);
	});

	test('our own programmatic edits are skipped', () => {
		assert.strictEqual(
			classifyDocumentChange({ contentChangeCount: 1, reason: undefined, isOwnEdit: true }),
			'skip-own-edit',
		);
	});

	test('undo is never treated as a rename', () => {
		assert.strictEqual(
			classifyDocumentChange({ contentChangeCount: 1, reason: textDocumentChangeReasonUndo, isOwnEdit: false }),
			'skip-undo-redo',
		);
	});

	test('redo is never treated as a rename', () => {
		assert.strictEqual(
			classifyDocumentChange({ contentChangeCount: 1, reason: textDocumentChangeReasonRedo, isOwnEdit: false }),
			'skip-undo-redo',
		);
	});

	test('undo with multiple content changes is still classified as undo', () => {
		assert.strictEqual(
			classifyDocumentChange({ contentChangeCount: 3, reason: textDocumentChangeReasonUndo, isOwnEdit: false }),
			'skip-undo-redo',
		);
	});

	test('multi-change transactions (multi-cursor typing shape) are skipped', () => {
		assert.strictEqual(
			classifyDocumentChange({ contentChangeCount: 2, reason: undefined, isOwnEdit: false }),
			'skip-multi-change',
		);
	});

	test('a single ordinary change is processed (debounced)', () => {
		assert.strictEqual(
			classifyDocumentChange({ contentChangeCount: 1, reason: undefined, isOwnEdit: false }),
			'process',
		);
	});
});

suite('offset mapping (provider path, W-C4)', () => {
	test('maps offsets after a shortening edit back to pre-edit coordinates', () => {
		const spans: ReplacementSpan[] = [{ startOffset: 10, endOffset: 15, newText: 'xy' }];

		assert.strictEqual(mapPostEditOffsetToPreEditOffset(5, spans), 5);
		assert.strictEqual(mapPostEditOffsetToPreEditOffset(20, spans), 23);
	});

	test('maps offsets after a lengthening edit back to pre-edit coordinates', () => {
		const spans: ReplacementSpan[] = [{ startOffset: 10, endOffset: 12, newText: 'abcdef' }];

		assert.strictEqual(mapPostEditOffsetToPreEditOffset(20, spans), 16);
	});

	test('clamps an offset inside replaced text to the replacement start', () => {
		const spans: ReplacementSpan[] = [{ startOffset: 10, endOffset: 15, newText: 'abcdefg' }];

		assert.strictEqual(mapPostEditOffsetToPreEditOffset(13, spans), 10);
	});

	test('accumulates deltas across multiple spans', () => {
		const spans: ReplacementSpan[] = [
			{ startOffset: 5, endOffset: 10, newText: 'ab' },
			{ startOffset: 20, endOffset: 22, newText: 'wxyz' },
		];

		// After span 1 (delta -3) and span 2 (delta +2): post 30 -> pre 31.
		assert.strictEqual(mapPostEditOffsetToPreEditOffset(30, spans), 31);
		// Between the two spans only the first delta applies: post 12 -> pre 15.
		assert.strictEqual(mapPostEditOffsetToPreEditOffset(12, spans), 15);
	});

	test('applyReplacementSpans applies spans regardless of input order', () => {
		const text = '0123456789';
		const spans: ReplacementSpan[] = [
			{ startOffset: 8, endOffset: 9, newText: 'Y' },
			{ startOffset: 1, endOffset: 3, newText: 'X' },
		];

		assert.strictEqual(applyReplacementSpans(text, spans), '0X34567Y9');
	});

	test('attribute insertion lands on the correct line when a reference precedes the declaration', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	void Update() { Debug.Log(maxDistance); }',
			'	[SerializeField] private float maxDistance = 100f;',
			'}',
			'',
		].join('\n');

		// Simulate an LSP rename of maxDistance -> d (both occurrences, shorter name).
		const spans: ReplacementSpan[] = [];
		let searchIndex = previousText.indexOf('maxDistance');
		while (searchIndex !== -1) {
			spans.push({ startOffset: searchIndex, endOffset: searchIndex + 'maxDistance'.length, newText: 'd' });
			searchIndex = previousText.indexOf('maxDistance', searchIndex + 1);
		}
		assert.strictEqual(spans.length, 2);

		const renamedText = applyReplacementSpans(previousText, spans);
		const insertions = buildFormerlySerializedAsEdits(previousText, renamedText);

		assert.ok(insertions.length > 0);

		// Map each insertion offset back to PRE-rename coordinates and apply the
		// rename and the insertions together, exactly like the provider does.
		const combinedSpans: ReplacementSpan[] = [
			...spans,
			...insertions.map((insertion) => {
				const preEditOffset = mapPostEditOffsetToPreEditOffset(insertion.offset, spans);
				return { startOffset: preEditOffset, endOffset: preEditOffset, newText: insertion.text };
			}),
		];
		const finalText = applyReplacementSpans(previousText, combinedSpans);

		assert.ok(finalText.includes('	[FormerlySerializedAs("maxDistance")]\n	[SerializeField] private float d = 100f;'));
		assert.ok(finalText.includes('using UnityEngine.Serialization;'));
		assert.ok(finalText.includes('Debug.Log(d);'));
	});
});

suite('passive rename gate (W-C2)', () => {
	const baseline = [
		'using UnityEngine;',
		'',
		'public class A : MonoBehaviour',
		'{',
		'	[SerializeField] private float speed = 1f;',
		'	[SerializeField] private int count = 1;',
		'',
		'	void Update() { Debug.Log(speed); }',
		'}',
		'',
	].join('\n');

	test('accepts a complete single-field rename', () => {
		const currentText = baseline.replace(/\bspeed\b/g, 'velocity');
		const rename = findSafePassiveRename(baseline, currentText);

		assert.ok(rename);
		assert.strictEqual(rename.previousName, 'speed');
		assert.strictEqual(rename.currentName, 'velocity');
	});

	test('rejects a partial rename that leaves the old name in code', () => {
		const currentText = baseline.replace('float speed', 'float velocity');

		assert.strictEqual(findSafePassiveRename(baseline, currentText), undefined);
	});

	test('rejects two simultaneous field renames', () => {
		const currentText = baseline
			.replace(/\bspeed\b/g, 'velocity')
			.replace(/\bcount\b/g, 'total');

		assert.strictEqual(findSafePassiveRename(baseline, currentText), undefined);
	});

	test('old name remaining only in a comment or string does not block the rename', () => {
		const withComment = baseline.replace(
			'	[SerializeField] private float speed = 1f;',
			'	// speed is the movement speed\n	[SerializeField] private float speed = 1f;',
		);
		const currentText = withComment
			.replace('float speed = 1f', 'float velocity = 1f')
			.replace('Debug.Log(speed)', 'Debug.Log(velocity)');

		const rename = findSafePassiveRename(withComment, currentText);

		assert.ok(rename);
		assert.strictEqual(rename.previousName, 'speed');
	});

	test('typing an unrelated statement is not a rename', () => {
		const currentText = baseline.replace(
			'	void Update() { Debug.Log(speed); }',
			'	void Update() { Debug.Log(speed); transform.Translate(Vector3.zero); }',
		);

		assert.strictEqual(findSafePassiveRename(baseline, currentText), undefined);
	});
});
