import { normalizeWhitespace, sanitizeSource, splitLines, stripLineComment, TextLine } from './textUtils';

const serializedFieldPattern = /\b(?:UnityEngine\.)?SerializeField(?:Attribute)?\b/;
const nonSerializedPattern = /\bNonSerialized(?:Attribute)?\b/;
// Base types whose public/serialized instance fields participate in Unity serialization.
const unityBasePattern = /\b(?:MonoBehaviour|ScriptableObject|StateMachineBehaviour)\b/;
const serializableAttributePattern = /\bSerializable(?:Attribute)?\b/;
// Type declarations that can own serialized fields (class, struct, record, record struct).
const typeDeclarationPattern = /\b(?:class|struct|record)\s+@?[A-Za-z_]\w*/;
// Statements and non-field members that would otherwise satisfy the field-shaped regex.
const nonFieldKeywordPattern = /^(?:using|namespace|return|throw|yield|goto|break|continue|case|default|else|do|delegate|event|operator|implicit|explicit|extern|base|this)\b/;

export type SerializedField = {
	name: string;
	serializedName: string;
	key: string;
	insertOffset: number;
	indent: string;
	attributesText: string;
};

// Innermost brace scope at the start of a line: either a type declaration
// (class/struct/record, with its Unity-serializability precomputed) or a
// non-type scope such as a namespace, method, property, or initializer body.
type TypeScope = {
	isType: boolean;
	serializable: boolean;
};

const parseCacheLimit = 8;
const parseCache = new Map<string, SerializedField[]>();

export function parseSerializedFields(text: string) {
	const cachedFields = parseCache.get(text);

	if (cachedFields) {
		parseCache.delete(text);
		parseCache.set(text, cachedFields);
		return cachedFields;
	}

	const fields = parseSerializedFieldsUncached(text);

	parseCache.set(text, fields);
	if (parseCache.size > parseCacheLimit) {
		const oldestKey = parseCache.keys().next().value;
		if (oldestKey !== undefined) {
			parseCache.delete(oldestKey);
		}
	}

	return fields;
}

function parseSerializedFieldsUncached(text: string): SerializedField[] {
	// Structure is parsed from the sanitized text (comments, strings, and
	// inactive #if branches blanked, offsets preserved) so code-shaped content
	// inside comments or strings can never produce phantom fields. Attribute
	// text is read from the original lines so real attribute arguments (for
	// FormerlySerializedAs dedup) survive.
	const sanitized = sanitizeSource(text);
	const originalLines = splitLines(text);
	const sanitizedLines = splitLines(sanitized);
	const scopeAtLine = computeLineScopes(sanitizedLines);
	const fields: SerializedField[] = [];

	for (let lineIndex = 0; lineIndex < sanitizedLines.length; lineIndex++) {
		const parsedField = parseSerializedFieldAtLine(originalLines, sanitizedLines, scopeAtLine, lineIndex);

		if (parsedField) {
			fields.push(parsedField);
		}
	}

	return fields;
}

function parseSerializedFieldAtLine(
	originalLines: TextLine[],
	sanitizedLines: TextLine[],
	scopeAtLine: (TypeScope | undefined)[],
	lineIndex: number,
): SerializedField | undefined {
	const sanitizedFieldLine = sanitizedLines[lineIndex].text.trimEnd();
	const inlineAttributes = getLeadingAttributes(sanitizedFieldLine);
	const declaration = sanitizedFieldLine.slice(inlineAttributes.length).trim();

	if (!declaration.endsWith(';')) {
		return undefined;
	}

	// Expression-bodied members (properties, indexers, event accessors) are not fields.
	if (declaration.includes('=>')) {
		return undefined;
	}

	if (nonFieldKeywordPattern.test(declaration)) {
		return undefined;
	}

	const declarationBeforeInitializer = declaration.split('=')[0];

	// Field-like event declarations (`public event Action changed;`) are not fields.
	if (/\bevent\b/.test(declarationBeforeInitializer)) {
		return undefined;
	}

	if (hasDisallowedDeclarationStructure(declarationBeforeInitializer)) {
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

	const attributeLineIndexes = collectAttributeLineIndexes(sanitizedLines, lineIndex);
	const attributeStartLine = attributeLineIndexes.length > 0 ? attributeLineIndexes[0] : lineIndex;
	const originalInlineAttributes = stripLineComment(originalLines[lineIndex].text).slice(0, inlineAttributes.length);
	const attributesAbove = attributeLineIndexes
		.map((attributeLineIndex) => stripLineComment(originalLines[attributeLineIndex].text))
		.join('\n');
	const attributesText = `${attributesAbove}\n${originalInlineAttributes}`;

	// A field explicitly opted out of serialization is never migrated.
	if (nonSerializedPattern.test(attributesText)) {
		return undefined;
	}

	// A field is Unity-serialized when it carries [SerializeField], or when it is a
	// public instance field directly inside a serializable Unity type (MonoBehaviour,
	// ScriptableObject, StateMachineBehaviour, or a [Serializable] type).
	const hasSerializeField = serializedFieldPattern.test(attributesText);

	if (!hasSerializeField) {
		const isPublic = /\bpublic\b/.test(modifiers);
		const scope = scopeAtLine[lineIndex];

		if (!isPublic || !scope || !scope.isType || !scope.serializable) {
			return undefined;
		}
	}

	const name = fieldMatch.groups.name;
	const serializedName = name.startsWith('@') ? name.slice(1) : name;
	const indent = originalLines[attributeStartLine].text.match(/^\s*/)?.[0] ?? '';

	return {
		name,
		serializedName,
		key: buildFieldKey(attributesText, modifiers, fieldMatch.groups.type, fieldMatch.groups.tail ?? ''),
		insertOffset: originalLines[attributeStartLine].offset,
		indent,
		attributesText,
	};
}

// Rejects declarations whose pre-initializer part contains parentheses, braces,
// or a top-level comma. Commas nested inside <> or [] are allowed so generic
// field types such as Dictionary<string, int> stay protected, while true
// multi-declarator fields (int a, b;) remain intentionally skipped.
function hasDisallowedDeclarationStructure(declarationBeforeInitializer: string) {
	let angleDepth = 0;
	let squareDepth = 0;

	for (const character of declarationBeforeInitializer) {
		if (character === '(' || character === '{') {
			return true;
		}

		if (character === '<') {
			angleDepth++;
		} else if (character === '>') {
			angleDepth = Math.max(0, angleDepth - 1);
		} else if (character === '[') {
			squareDepth++;
		} else if (character === ']') {
			squareDepth = Math.max(0, squareDepth - 1);
		} else if (character === ',' && angleDepth === 0 && squareDepth === 0) {
			return true;
		}
	}

	return false;
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

// Walks upward from the field line collecting attribute-only lines. Comment
// lines and blank lines between the attributes and the field are skipped
// (attributes still apply across them) but are not part of the result, so the
// insertion point stays on the topmost real attribute line.
function collectAttributeLineIndexes(sanitizedLines: TextLine[], fieldLineIndex: number) {
	const attributeLineIndexes: number[] = [];

	for (let lineIndex = fieldLineIndex - 1; lineIndex >= 0; lineIndex--) {
		const trimmedLine = sanitizedLines[lineIndex].text.trim();

		if (trimmedLine === '') {
			continue;
		}

		if (trimmedLine.startsWith('[') && trimmedLine.endsWith(']')) {
			attributeLineIndexes.unshift(lineIndex);
			continue;
		}

		break;
	}

	return attributeLineIndexes;
}

// Computes the innermost brace scope at the start of every line by tracking
// { and } through the sanitized text. Each opening brace is classified by the
// header text accumulated since the previous ; { or }, so a field is resolved
// against its actual enclosing type instead of the nearest preceding
// class/struct keyword (audit W-C6), and nested types no longer leak their
// serializability to following outer-type fields.
function computeLineScopes(sanitizedLines: TextLine[]): (TypeScope | undefined)[] {
	const scopeAtLine: (TypeScope | undefined)[] = [];
	const stack: TypeScope[] = [];
	let header = '';

	for (const line of sanitizedLines) {
		scopeAtLine.push(stack.length > 0 ? stack[stack.length - 1] : undefined);

		for (const character of line.text) {
			if (character === '{') {
				stack.push(createScope(header));
				header = '';
			} else if (character === '}') {
				stack.pop();
				header = '';
			} else if (character === ';') {
				header = '';
			} else {
				header += character;
			}
		}

		header += ' ';
	}

	return scopeAtLine;
}

function createScope(header: string): TypeScope {
	if (!typeDeclarationPattern.test(header)) {
		return { isType: false, serializable: false };
	}

	return {
		isType: true,
		serializable: unityBasePattern.test(header) || serializableAttributePattern.test(header),
	};
}

function getLeadingAttributes(line: string) {
	return line.match(/^\s*(?:\[[^\]\r\n]*\]\s*)+/)?.[0] ?? '';
}
