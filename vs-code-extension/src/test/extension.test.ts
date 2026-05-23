import * as assert from 'assert';

import { buildFormerlySerializedAsEdits, findRenamedSerializedFields, TextInsertion } from '../formerlySerializedAs';

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

	test('reports renamed serialized field names', () => {
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

		const [rename] = findRenamedSerializedFields(previousText, currentText);

		assert.strictEqual(rename.previousName, 'maxDistance');
		assert.strictEqual(rename.currentName, 'attackDistance');
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

	test('handles rename when class contains multiple serialized fields of same type', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class TestSerialized : MonoBehaviour',
			'{',
			'	[SerializeField] private string m_PlayerName;',
			'	[SerializeField] private int m_PlayerLevel;',
			'	[SerializeField] private string m_EnemyName;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('m_PlayerName', 'm_PlayerName1');
		const updatedText = applyInsertions(currentText, buildFormerlySerializedAsEdits(previousText, currentText));

		assert.ok(updatedText.includes('[FormerlySerializedAs("m_PlayerName")]\n\t[SerializeField] private string m_PlayerName1;'));
	});
});

function applyInsertions(text: string, insertions: TextInsertion[]) {
	return [...insertions]
		.sort((left, right) => right.offset - left.offset)
		.reduce((updatedText, insertion) => {
			return `${updatedText.slice(0, insertion.offset)}${insertion.text}${updatedText.slice(insertion.offset)}`;
		}, text);
}
