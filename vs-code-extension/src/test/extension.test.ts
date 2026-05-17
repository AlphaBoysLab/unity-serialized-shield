import * as assert from 'assert';

import { buildFormerlySerializedAsEdits, TextInsertion } from '../formerlySerializedAs';

suite('UnitySerializedShield', () => {
	test('adds FormerlySerializedAs when a SerializeField variable is renamed', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class EnemySensor : MonoBehaviour',
			'{',
			'	[SerializeField] private float maxDistance = 100f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('maxDistance', 'attackDistance');
		const updatedText = applyInsertions(currentText, buildFormerlySerializedAsEdits(previousText, currentText));

		assert.ok(updatedText.includes('using UnityEngine.Serialization;'));
		assert.ok(updatedText.includes('[FormerlySerializedAs("maxDistance")]\n\t[SerializeField] private float attackDistance = 100f;'));
	});

	test('does not add a duplicate FormerlySerializedAs attribute', () => {
		const previousText = [
			'using UnityEngine;',
			'using UnityEngine.Serialization;',
			'',
			'public class EnemySensor : MonoBehaviour',
			'{',
			'	[SerializeField] private float maxDistance = 100f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace(
			'	[SerializeField] private float maxDistance = 100f;',
			'	[FormerlySerializedAs("maxDistance")]\n	[SerializeField] private float attackDistance = 100f;',
		);

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});

	test('ignores non-serialized variables', () => {
		const previousText = [
			'public class PlainClass',
			'{',
			'	private float maxDistance = 100f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('maxDistance', 'attackDistance');

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});
});

function applyInsertions(text: string, insertions: TextInsertion[]) {
	return [...insertions]
		.sort((left, right) => right.offset - left.offset)
		.reduce((updatedText, insertion) => {
			return `${updatedText.slice(0, insertion.offset)}${insertion.text}${updatedText.slice(insertion.offset)}`;
		}, text);
}
