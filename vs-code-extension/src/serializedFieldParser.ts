import { normalizeWhitespace, splitLines, stripLineComment, TextLine } from './textUtils';

const serializedFieldPattern = /\b(?:UnityEngine\.)?SerializeField(?:Attribute)?\b/;

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

	if (!serializedFieldPattern.test(attributesText)) {
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

	if (/\b(?:static|const)\b/.test(modifiers)) {
		return undefined;
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

function getLeadingAttributes(line: string) {
	return line.match(/^\s*(?:\[[^\]\r\n]*\]\s*)+/)?.[0] ?? '';
}
