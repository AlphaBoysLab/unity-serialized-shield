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

	test('detects rename on prefix deletion (from start of variable name)', () => {
		const previousText = [
			'using UnityEngine;',
			'public class EnemySensor : MonoBehaviour',
			'{',
			'	[SerializeField] private float m_maxDistance = 100f;',
			'}',
		].join('\n');
		const currentText = previousText.replace('m_maxDistance', 'maxDistance');
		const [rename] = findRenamedSerializedFields(previousText, currentText);

		assert.strictEqual(rename.previousName, 'm_maxDistance');
		assert.strictEqual(rename.currentName, 'maxDistance');
	});

	test('detects rename on suffix digit replacement (from end of variable name)', () => {
		const previousText = [
			'using UnityEngine;',
			'public class EnemySensor : MonoBehaviour',
			'{',
			'	[SerializeField] private float maxDistance5 = 100f;',
			'}',
		].join('\n');
		const currentText = previousText.replace('maxDistance5', 'maxDistance6');
		const [rename] = findRenamedSerializedFields(previousText, currentText);

		assert.strictEqual(rename.previousName, 'maxDistance5');
		assert.strictEqual(rename.currentName, 'maxDistance6');
	});

	test('protects a public field in a MonoBehaviour without [SerializeField]', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class Player : MonoBehaviour',
			'{',
			'	public float speed = 5f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('speed', 'moveSpeed');
		const updatedText = applyInsertions(currentText, buildFormerlySerializedAsEdits(previousText, currentText));

		assert.ok(updatedText.includes('using UnityEngine.Serialization;'));
		assert.ok(updatedText.includes('[FormerlySerializedAs("speed")]\n\tpublic float moveSpeed = 5f;'));
	});

	test('protects a public field in a [Serializable] type', () => {
		const previousText = [
			'using System;',
			'',
			'[Serializable]',
			'public class Stats',
			'{',
			'	public int health = 10;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('health', 'maxHealth');
		const [rename] = findRenamedSerializedFields(previousText, currentText);

		assert.strictEqual(rename.previousName, 'health');
		assert.strictEqual(rename.currentName, 'maxHealth');
	});

	test('ignores a public field in a plain non-Unity class', () => {
		const previousText = [
			'public class PlainData',
			'{',
			'	public float speed = 5f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('speed', 'moveSpeed');

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});

	test('ignores a readonly serialized field', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class Config : MonoBehaviour',
			'{',
			'	[SerializeField] private readonly float maxDistance = 100f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('maxDistance', 'attackDistance');

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});

	test('ignores a [NonSerialized] public field in a MonoBehaviour', () => {
		const previousText = [
			'using System;',
			'using UnityEngine;',
			'',
			'public class Config : MonoBehaviour',
			'{',
			'	[NonSerialized] public float maxDistance = 100f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('maxDistance', 'attackDistance');

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});
});

suite('UnitySerializedShield using-directive and alias handling', () => {
	test('a using directive inside a comment does not suppress the real using insertion', () => {
		const previousText = [
			'using UnityEngine;',
			'// using UnityEngine.Serialization;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private float speed = 1f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('float speed', 'float velocity');
		const insertions = buildFormerlySerializedAsEdits(previousText, currentText);
		const updatedText = applyInsertions(currentText, insertions);

		assert.ok(insertions.some((insertion) => insertion.text.includes('using UnityEngine.Serialization;')));
		assert.ok(/^using UnityEngine\.Serialization;$/m.test(updatedText));
		assert.ok(updatedText.includes('[FormerlySerializedAs("speed")]'));
	});

	test('an existing FormerlySerializedAs via a namespace alias is not duplicated', () => {
		const previousText = [
			'using UnityEngine;',
			'using US = UnityEngine.Serialization;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private float maxDistance = 100f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace(
			'	[SerializeField] private float maxDistance = 100f;',
			'	[US.FormerlySerializedAs("maxDistance")]\n	[SerializeField] private float attackDistance = 100f;',
		);

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});

	test('an existing FormerlySerializedAs via an attribute alias is not duplicated', () => {
		const previousText = [
			'using UnityEngine;',
			'using FSA = UnityEngine.Serialization.FormerlySerializedAsAttribute;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private float maxDistance = 100f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace(
			'	[SerializeField] private float maxDistance = 100f;',
			'	[FSA("maxDistance")]\n	[SerializeField] private float attackDistance = 100f;',
		);

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});

	test('a fully qualified FormerlySerializedAs is not duplicated', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private float maxDistance = 100f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace(
			'	[SerializeField] private float maxDistance = 100f;',
			'	[UnityEngine.Serialization.FormerlySerializedAs("maxDistance")]\n	[SerializeField] private float attackDistance = 100f;',
		);

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});

	test('an alias-only using still gets the plain using inserted (compilability)', () => {
		const previousText = [
			'using UnityEngine;',
			'using US = UnityEngine.Serialization;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private float speed = 1f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('float speed', 'float velocity');
		const insertions = buildFormerlySerializedAsEdits(previousText, currentText);

		assert.ok(insertions.some((insertion) => insertion.text.includes('using UnityEngine.Serialization;')));
	});

	test('a chained rename keeps the original attribute and records the intermediate name', () => {
		const previousText = [
			'using UnityEngine;',
			'using UnityEngine.Serialization;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[FormerlySerializedAs("origName")]',
			'	[SerializeField] private float second = 1f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('float second', 'float third');
		const updatedText = applyInsertions(currentText, buildFormerlySerializedAsEdits(previousText, currentText));

		assert.ok(updatedText.includes('[FormerlySerializedAs("origName")]'));
		assert.ok(updatedText.includes('[FormerlySerializedAs("second")]'));
		assert.ok(updatedText.includes('float third = 1f;'));
	});
});

function applyInsertions(text: string, insertions: TextInsertion[]) {
	return [...insertions]
		.sort((left, right) => right.offset - left.offset)
		.reduce((updatedText, insertion) => {
			return `${updatedText.slice(0, insertion.offset)}${insertion.text}${updatedText.slice(insertion.offset)}`;
		}, text);
}
