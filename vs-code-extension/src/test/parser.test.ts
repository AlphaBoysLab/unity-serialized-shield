import * as assert from 'assert';

import { parseSerializedFields } from '../serializedFieldParser';
import { buildFormerlySerializedAsEdits, findRenamedSerializedFields, TextInsertion } from '../formerlySerializedAs';
import { detectLineEnding, sanitizeSource } from '../textUtils';

suite('serializedFieldParser', () => {
	test('parses generic field types containing commas (Dictionary<string, int>)', () => {
		const text = [
			'using System.Collections.Generic;',
			'using UnityEngine;',
			'',
			'public class Registry : MonoBehaviour',
			'{',
			'	[SerializeField] private Dictionary<string, int> table = new Dictionary<string, int>();',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'table');
	});

	test('still skips multi-declarator fields', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private int a, b;',
			'}',
			'',
		].join('\n');

		assert.strictEqual(parseSerializedFields(text).length, 0);
	});

	// Audit VS Code R3: an attribute list wrapped across lines must still be
	// recognized so the field stays protected.
	test('parses a field whose attribute list wraps across lines', () => {
		const text = [
			'using UnityEngine;',
			'using UnityEngine.Serialization;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField,',
			'	 FormerlySerializedAs("q")]',
			'	private int score = 1;',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'score');
	});

	test('does not mistake a bracketed statement above a field for an attribute', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	private int Compute() { return data[0]; }',
			'	[SerializeField] private int score = 1;',
			'}',
			'',
		].join('\n');

		// score is still parsed; the `data[0]` line is not swallowed as an attribute.
		const fields = parseSerializedFields(text);
		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'score');
	});

	test('does not treat expression-bodied properties as fields', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private float speed = 1f;',
			'	public float Speed => speed;',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'speed');
	});

	test('does not treat event declarations as fields', () => {
		const text = [
			'using System;',
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	public event Action changed;',
			'}',
			'',
		].join('\n');

		assert.strictEqual(parseSerializedFields(text).length, 0);
	});

	test('associates attributes across comment lines between attribute and field', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField]',
			'	// tuning value',
			'	private float speed = 1f;',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'speed');
		assert.ok(fields[0].attributesText.includes('SerializeField'));
		// The insertion point is the topmost attribute line, not the comment line.
		assert.strictEqual(text.slice(fields[0].insertOffset).startsWith('	[SerializeField]'), true);
	});

	test('ignores fields inside block comments', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class Real : MonoBehaviour',
			'{',
			'	/*',
			'	[SerializeField] private int ghost = 1;',
			'	*/',
			'	[SerializeField] private int actual = 1;',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'actual');
	});

	test('a class name inside a string does not make following fields serializable', () => {
		const text = [
			'public class Fake',
			'{',
			'	private string s = "class Foo : MonoBehaviour {";',
			'	public int x = 1;',
			'}',
			'',
		].join('\n');

		assert.strictEqual(parseSerializedFields(text).length, 0);
	});

	test('ignores field-shaped lines inside multi-line verbatim strings', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	private string sql = @"',
			'	public int notAField = 1;',
			'	";',
			'	[SerializeField] private float speed = 1f;',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'speed');
	});

	test('keeps only the first branch of #if/#else groups (no phantom duplicates)', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'#if UNITY_EDITOR',
			'	[SerializeField] private float speed = 1f;',
			'#else',
			'	[SerializeField] private float speed = 1f;',
			'#endif',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'speed');
	});

	test('resolves the enclosing type with brace awareness (field after a nested type)', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class Outer : MonoBehaviour',
			'{',
			'	public class Inner',
			'	{',
			'	}',
			'',
			'	public int hp = 1;',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'hp');
	});

	test('does not protect public fields of a plain nested class inside a MonoBehaviour', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class Outer : MonoBehaviour',
			'{',
			'	public class Plain',
			'	{',
			'		public int value = 1;',
			'	}',
			'}',
			'',
		].join('\n');

		assert.strictEqual(parseSerializedFields(text).length, 0);
	});

	test('protects public fields of a [Serializable] nested class', () => {
		const text = [
			'using System;',
			'using UnityEngine;',
			'',
			'public class Outer : MonoBehaviour',
			'{',
			'	[Serializable]',
			'	public class Settings',
			'	{',
			'		public int level = 1;',
			'	}',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'level');
	});

	test('does not treat local variables inside methods as serialized public fields', () => {
		const text = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	void Update()',
			'	{',
			'		public int looksLikeAField = 1;',
			'	}',
			'}',
			'',
		].join('\n');

		assert.strictEqual(parseSerializedFields(text).length, 0);
	});

	test('supports [Serializable] record struct type declarations', () => {
		const text = [
			'using System;',
			'',
			'[Serializable]',
			'public record struct Damage',
			'{',
			'	public int amount;',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'amount');
	});

	test('supports file-scoped namespaces', () => {
		const text = [
			'using UnityEngine;',
			'',
			'namespace Game;',
			'',
			'public class Player : MonoBehaviour',
			'{',
			'	[SerializeField] private int hp = 1;',
			'}',
			'',
		].join('\n');

		const fields = parseSerializedFields(text);

		assert.strictEqual(fields.length, 1);
		assert.strictEqual(fields[0].name, 'hp');
	});
});

suite('sanitizeSource', () => {
	test('blanks comments and strings but preserves offsets and newlines', () => {
		const text = 'int a; // comment\nstring s = "class X";\n/* block */ int b;\n';
		const sanitized = sanitizeSource(text);

		assert.strictEqual(sanitized.length, text.length);
		assert.strictEqual(sanitized.split('\n').length, text.split('\n').length);
		assert.ok(!sanitized.includes('comment'));
		assert.ok(!sanitized.includes('class X'));
		assert.ok(!sanitized.includes('block'));
		assert.ok(sanitized.includes('int a;'));
		assert.ok(sanitized.includes('int b;'));
		assert.strictEqual(sanitized.indexOf('int b;'), text.indexOf('int b;'));
	});

	test('blanks inactive #else branches but keeps the first #if branch', () => {
		const text = '#if A\nint first;\n#else\nint second;\n#endif\n';
		const sanitized = sanitizeSource(text);

		assert.ok(sanitized.includes('int first;'));
		assert.ok(!sanitized.includes('int second;'));
		assert.strictEqual(sanitized.length, text.length);
	});

	// Audit VS Code N1: code inside an interpolation hole is live and must survive
	// (the braces themselves are blanked; only the hole's code is kept).
	test('preserves code inside interpolation holes but blanks literal text', () => {
		const text = 'void L() { Debug.Log($"total {speed} done"); }\n';
		const sanitized = sanitizeSource(text);

		assert.strictEqual(sanitized.length, text.length);
		assert.ok(sanitized.includes('speed'), 'hole identifier kept');
		assert.ok(!sanitized.includes('total'), 'literal string text blanked');
		assert.ok(!sanitized.includes('done'), 'literal string text blanked');
		// Only the hole occurrence of "speed" remains, at its original offset.
		assert.strictEqual(sanitized.indexOf('speed'), text.indexOf('speed'));
	});

	test('preserves interpolation holes in verbatim and nested-brace holes', () => {
		const text = 'var s = $@"x={obj.Value} y={dict[key]}";\n';
		const sanitized = sanitizeSource(text);

		assert.strictEqual(sanitized.length, text.length);
		assert.ok(sanitized.includes('obj.Value'));
		assert.ok(sanitized.includes('dict[key]'));
		assert.ok(!sanitized.includes('x='), 'literal blanked');
	});

	test('escaped braces are not treated as interpolation holes', () => {
		const text = 'var s = $"{{literal}} {speed}";\n';
		const sanitized = sanitizeSource(text);

		assert.strictEqual(sanitized.length, text.length);
		assert.ok(!sanitized.includes('literal'), 'escaped {{ }} is literal text, blanked');
		assert.ok(sanitized.includes('speed'), 'real hole kept');
	});
});

suite('detectLineEnding', () => {
	test('majority LF wins over a stray CRLF', () => {
		assert.strictEqual(detectLineEnding('a\nb\nc\nd\r\ne\n'), '\n');
	});

	test('majority CRLF wins', () => {
		assert.strictEqual(detectLineEnding('a\r\nb\r\nc\n'), '\r\n');
	});
});

suite('rename detection with hardened parser', () => {
	test('renaming a Dictionary<string, int> field is protected', () => {
		const previousText = [
			'using System.Collections.Generic;',
			'using UnityEngine;',
			'',
			'public class Registry : MonoBehaviour',
			'{',
			'	[SerializeField] private Dictionary<string, int> table = new Dictionary<string, int>();',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace(/\btable\b/g, 'lookup');
		const updatedText = applyInsertions(currentText, buildFormerlySerializedAsEdits(previousText, currentText));

		assert.ok(updatedText.includes('[FormerlySerializedAs("table")]'));
	});

	test('renaming a field duplicated across #if/#else adds exactly one attribute', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'#if UNITY_EDITOR',
			'	[SerializeField] private float speed = 1f;',
			'#else',
			'	[SerializeField] private float speed = 1f;',
			'#endif',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace(/\bspeed\b/g, 'velocity');
		const insertions = buildFormerlySerializedAsEdits(previousText, currentText);
		const attributeInsertions = insertions.filter((insertion) => insertion.text.includes('FormerlySerializedAs'));

		assert.strictEqual(attributeInsertions.length, 1);
	});

	test('renaming a field with a comment between attribute and declaration works', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField]',
			'	// tuning value',
			'	private float speed = 1f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('float speed', 'float velocity');
		const updatedText = applyInsertions(currentText, buildFormerlySerializedAsEdits(previousText, currentText));

		assert.ok(updatedText.includes('[FormerlySerializedAs("speed")]\n	[SerializeField]'));
	});

	test('renaming a property (expression-bodied) adds nothing', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private float speed = 1f;',
			'	public float Speed => speed;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('public float Speed', 'public float Velocity');

		assert.deepStrictEqual(buildFormerlySerializedAsEdits(previousText, currentText), []);
	});

	test('renaming a nested-type field after the nested type is attributed to the outer type', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class Outer : MonoBehaviour',
			'{',
			'	public class Inner',
			'	{',
			'	}',
			'',
			'	public int hp = 1;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('int hp', 'int health');
		const [rename] = findRenamedSerializedFields(previousText, currentText);

		assert.ok(rename);
		assert.strictEqual(rename.previousName, 'hp');
		assert.strictEqual(rename.currentName, 'health');
	});

	test('renaming a [Serializable] record struct field is protected', () => {
		const previousText = [
			'using System;',
			'',
			'[Serializable]',
			'public record struct Damage',
			'{',
			'	public int amount;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('int amount', 'int value');
		const [rename] = findRenamedSerializedFields(previousText, currentText);

		assert.ok(rename);
		assert.strictEqual(rename.previousName, 'amount');
	});

	test('uses CRLF for inserted lines in a CRLF document', () => {
		const previousText = [
			'using UnityEngine;',
			'',
			'public class A : MonoBehaviour',
			'{',
			'	[SerializeField] private float speed = 1f;',
			'}',
			'',
		].join('\r\n');
		const currentText = previousText.replace('float speed', 'float velocity');
		const insertions = buildFormerlySerializedAsEdits(previousText, currentText);

		assert.ok(insertions.length > 0);
		for (const insertion of insertions) {
			assert.ok(insertion.text.endsWith('\r\n'));
		}
	});

	test('inserts the using directive after the BOM in a BOM-prefixed document', () => {
		const previousText = '﻿' + [
			'public class A : UnityEngine.MonoBehaviour',
			'{',
			'	[UnityEngine.SerializeField] private float speed = 1f;',
			'}',
			'',
		].join('\n');
		const currentText = previousText.replace('float speed', 'float velocity');
		const insertions = buildFormerlySerializedAsEdits(previousText, currentText);
		const usingInsertion = insertions.find((insertion) => insertion.text.includes('using UnityEngine.Serialization;'));

		assert.ok(usingInsertion);
		assert.strictEqual(usingInsertion.offset, 1);
	});
});

function applyInsertions(text: string, insertions: TextInsertion[]) {
	return [...insertions]
		.sort((left, right) => right.offset - left.offset)
		.reduce((updatedText, insertion) => {
			return `${updatedText.slice(0, insertion.offset)}${insertion.text}${updatedText.slice(insertion.offset)}`;
		}, text);
}
