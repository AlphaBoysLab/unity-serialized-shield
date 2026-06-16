import { normalizeWhitespace, splitLines, stripLineComment, TextLine } from './textUtils';

const serializedFieldPattern = /\b(?:UnityEngine\.)?SerializeField(?:Attribute)?\b/;
const nonSerializedPattern = /\bNonSerialized(?:Attribute)?\b/;
// Base types whose public/serialized instance fields participate in Unity serialization.
const unityBasePattern = /\b(?:MonoBehaviour|ScriptableObject|StateMachineBehaviour)\b/;
const serializableAttributePattern = /\bSerializable(?:Attribute)?\b/;

export type SerializedField = {
	name: string;
	serializedName: string;
	key: string;
	insertOffset: number;
	indent: string;
	attributesText: string;
};

export function parseSerializedFields(text: string) {
	const lines = splitLines(text);
	const fields: SerializedField[] = [];

	for (let lineIndex = 0; lineIndex < lines.length; lineIndex++) {
		const parsedField = parseSerializedFieldAtLine(lines, lineIndex);

		if (parsedField) {
			fields.push(parsedField);
		}
	}

	return fields;
}

function parseSerializedFieldAtLine(lines: TextLine[], lineIndex: number): SerializedField | undefined {
	const fieldLine = stripLineComment(lines[lineIndex].text).trimEnd();
	const inlineAttributes = getLeadingAttributes(fieldLine);
	const declaration = fieldLine.slice(inlineAttributes.length).trim();

	if (!declaration.endsWith(';')) {
		return undefined;
	}

	const attributeStartLine = findAttributeStartLine(lines, lineIndex);
	const attributesAbove = lines
		.slice(attributeStartLine, lineIndex)
		.map((line) => line.text)
		.join('\n');
	const attributesText = `${attributesAbove}\n${inlineAttributes}`;

	// A field explicitly opted out of serialization is never migrated.
	if (nonSerializedPattern.test(attributesText)) {
		return undefined;
	}

	const declarationBeforeInitializer = declaration.split('=')[0];

	if (declarationBeforeInitializer.includes('(') || declarationBeforeInitializer.includes('{') || declarationBeforeInitializer.includes(',')) {
		return undefined;
	}

	const fieldMatch = /^(?:(?<modifiers>(?:(?:public|private|protected|internal|static|readonly|const|volatile|new|unsafe)\s+)*))?(?<type>.+?)\s+(?<name>@?[A-Za-z_]\w*)\s*(?<tail>=.*)?;$/.exec(declaration);

	if (!fieldMatch?.groups) {
		return undefined;
	}

	const modifiers = fieldMatch.groups.modifiers ?? '';

	// Unity never serializes static, const, or readonly fields.
	if (/\b(?:static|const|readonly)\b/.test(modifiers)) {
		return undefined;
	}

	// A field is Unity-serialized when it carries [SerializeField], or when it is a
	// public instance field inside a serializable Unity type (MonoBehaviour,
	// ScriptableObject, StateMachineBehaviour, or a [Serializable] type).
	const hasSerializeField = serializedFieldPattern.test(attributesText);

	if (!hasSerializeField) {
		const isPublic = /\bpublic\b/.test(modifiers);

		if (!isPublic || !isEnclosingTypeSerializable(lines, attributeStartLine)) {
			return undefined;
		}
	}

	const name = fieldMatch.groups.name;
	const serializedName = name.startsWith('@') ? name.slice(1) : name;
	const indent = lines[attributeStartLine].text.match(/^\s*/)?.[0] ?? '';

	return {
		name,
		serializedName,
		key: buildFieldKey(attributesText, modifiers, fieldMatch.groups.type, fieldMatch.groups.tail ?? ''),
		insertOffset: lines[attributeStartLine].offset,
		indent,
		attributesText,
	};
}

function buildFieldKey(attributesText: string, modifiers: string, typeName: string, tail: string) {
	const normalizedAttributes = normalizeWhitespace(
		attributesText.replace(/\[[^\]\r\n]*FormerlySerializedAs(?:Attribute)?[^\]\r\n]*\]/g, ''),
	);
	const normalizedModifiers = normalizeWhitespace(modifiers);
	const normalizedType = normalizeWhitespace(typeName);
	const normalizedTail = normalizeWhitespace(tail);

	return `${normalizedAttributes}|${normalizedModifiers}|${normalizedType}|${normalizedTail}`;
}

function findAttributeStartLine(lines: TextLine[], fieldLineIndex: number) {
	let lineIndex = fieldLineIndex;

	while (lineIndex > 0 && isAttributeOnlyLine(lines[lineIndex - 1].text)) {
		lineIndex--;
	}

	return lineIndex;
}

function isAttributeOnlyLine(line: string) {
	const trimmedLine = stripLineComment(line).trim();

	return trimmedLine.startsWith('[') && trimmedLine.endsWith(']');
}

// Determines whether the type enclosing the given line is one whose public
// instance fields Unity serializes. Uses the nearest preceding class/struct
// declaration (with its base list, possibly spanning lines, and any preceding
// [Serializable] attribute), which covers the common single-type-per-file layout
// of Unity scripts.
function isEnclosingTypeSerializable(lines: TextLine[], fieldLineIndex: number) {
	for (let lineIndex = fieldLineIndex - 1; lineIndex >= 0; lineIndex--) {
		const trimmedLine = stripLineComment(lines[lineIndex].text).trim();

		if (!/\b(?:class|struct)\s+[A-Za-z_]\w*/.test(trimmedLine)) {
			continue;
		}

		// Collect the declaration and any continuation up to the opening brace so a
		// multi-line base list (e.g. `class X :` then `MonoBehaviour {`) is captured.
		let declarationText = '';
		for (let scanIndex = lineIndex; scanIndex < lines.length; scanIndex++) {
			declarationText += ` ${stripLineComment(lines[scanIndex].text)}`;
			if (lines[scanIndex].text.includes('{')) {
				break;
			}
		}

		if (unityBasePattern.test(declarationText)) {
			return true;
		}

		// Otherwise check for a [Serializable] attribute on the lines above the type.
		for (let attributeIndex = lineIndex - 1; attributeIndex >= 0 && isAttributeOnlyLine(lines[attributeIndex].text); attributeIndex--) {
			if (serializableAttributePattern.test(stripLineComment(lines[attributeIndex].text))) {
				return true;
			}
		}

		return false;
	}

	return false;
}

function getLeadingAttributes(line: string) {
	return line.match(/^\s*(?:\[[^\]\r\n]*\]\s*)+/)?.[0] ?? '';
}
