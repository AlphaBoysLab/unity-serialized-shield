import { parseSerializedFields, SerializedField } from './serializedFieldParser';
import {
	detectLineEnding,
	escapeCsharpString,
	escapeRegExp,
	identifierOccursInCode,
	sanitizeSource,
	splitLines,
	TextInsertion,
} from './textUtils';

export type { TextInsertion } from './textUtils';

const serializationUsing = 'using UnityEngine.Serialization;';
// A plain (or global) using of the namespace. An alias directive such as
// `using UES = UnityEngine.Serialization;` intentionally does NOT match: the
// short attribute name would not compile with only an alias in scope, so the
// plain using still needs to be inserted.
const serializationUsingPattern = /\b(?:global\s+)?using\s+(?:global::)?UnityEngine\.Serialization\s*;/;
// `using Alias = UnityEngine.Serialization.FormerlySerializedAsAttribute;`
const attributeAliasPattern = /\busing\s+(@?[A-Za-z_]\w*)\s*=\s*(?:global::)?UnityEngine\.Serialization\.FormerlySerializedAs(?:Attribute)?\s*;/g;
// `using Alias = UnityEngine.Serialization;`
const namespaceAliasPattern = /\busing\s+(@?[A-Za-z_]\w*)\s*=\s*(?:global::)?UnityEngine\.Serialization\s*;/g;

export type SerializedFieldRename = {
	previousName: string;
	previousSerializedName: string;
	currentName: string;
	currentSerializedName: string;
	currentField: SerializedField;
};

type FormerlySerializedAsAliases = {
	attributeAliases: string[];
	namespaceAliases: string[];
};

export function buildFormerlySerializedAsEdits(previousText: string, currentText: string): TextInsertion[] {
	const renames = findRenamedSerializedFields(previousText, currentText);
	const eol = detectLineEnding(currentText);
	const insertions: TextInsertion[] = [];
	const sanitizedCurrentText = sanitizeSource(currentText);
	const aliases = collectFormerlySerializedAsAliases(sanitizedCurrentText);

	for (const rename of renames) {
		if (hasFormerlySerializedAs(rename.currentField.attributesText, rename.previousSerializedName, aliases)) {
			continue;
		}

		insertions.push({
			offset: rename.currentField.insertOffset,
			text: `${rename.currentField.indent}[FormerlySerializedAs("${escapeCsharpString(rename.previousSerializedName)}")]${eol}`,
		});
	}

	// The using check runs on sanitized text so a using directive mentioned in a
	// comment or string can no longer suppress the real insertion (audit W-C14).
	if (insertions.length > 0 && !serializationUsingPattern.test(sanitizedCurrentText)) {
		insertions.unshift({
			offset: findUsingInsertOffset(currentText, sanitizedCurrentText),
			text: `${serializationUsing}${eol}`,
		});
	}

	return insertions;
}

export function findRenamedSerializedFields(previousText: string, currentText: string): SerializedFieldRename[] {
	const previousFields = fieldsByKey(parseSerializedFields(previousText));
	const currentFields = fieldsByKey(parseSerializedFields(currentText));
	const renames: SerializedFieldRename[] = [];

	for (const [key, previousGroup] of previousFields) {
		const currentGroup = currentFields.get(key);

		if (!currentGroup || currentGroup.length !== previousGroup.length) {
			continue;
		}

		for (let i = 0; i < previousGroup.length; i++) {
			const previousField = previousGroup[i];
			const currentField = currentGroup[i];

			if (currentField.name === previousField.name || currentField.serializedName === previousField.serializedName) {
				continue;
			}

			renames.push({
				previousName: previousField.name,
				previousSerializedName: previousField.serializedName,
				currentName: currentField.name,
				currentSerializedName: currentField.serializedName,
				currentField,
			});
		}
	}

	return renames;
}

// Strict gate for the passive (text-diff) rename listener. A diff is accepted
// as a rename only when EXACTLY one serialized field changed its name AND the
// old name no longer occurs as an identifier anywhere in the current code
// (comments, strings, and inactive #if branches are ignored). Anything else —
// plain typing that happens to keep the field shape, partial renames that left
// references behind, multiple simultaneous changes — is rejected.
export function findSafePassiveRename(previousText: string, currentText: string): SerializedFieldRename | undefined {
	const renames = findRenamedSerializedFields(previousText, currentText);

	if (renames.length !== 1) {
		return undefined;
	}

	const [rename] = renames;

	if (identifierOccursInCode(currentText, rename.previousSerializedName)) {
		return undefined;
	}

	return rename;
}

function fieldsByKey(fields: SerializedField[]): Map<string, SerializedField[]> {
	const groupedFields = new Map<string, SerializedField[]>();

	for (const field of fields) {
		const existingFields = groupedFields.get(field.key) ?? [];
		existingFields.push(field);
		groupedFields.set(field.key, existingFields);
	}

	return groupedFields;
}

function collectFormerlySerializedAsAliases(sanitizedText: string): FormerlySerializedAsAliases {
	const attributeAliases: string[] = [];
	const namespaceAliases: string[] = [];
	let match: RegExpExecArray | null;

	attributeAliasPattern.lastIndex = 0;
	while ((match = attributeAliasPattern.exec(sanitizedText)) !== null) {
		attributeAliases.push(match[1]);
	}

	namespaceAliasPattern.lastIndex = 0;
	while ((match = namespaceAliasPattern.exec(sanitizedText)) !== null) {
		namespaceAliases.push(match[1]);
	}

	return { attributeAliases, namespaceAliases };
}

function hasFormerlySerializedAs(attributesText: string, previousName: string, aliases: FormerlySerializedAsAliases) {
	const escapedName = escapeRegExp(previousName);
	const namespacePrefixes = [
		'(?:global::)?UnityEngine\\.Serialization\\.',
		...aliases.namespaceAliases.map((alias) => `${escapeRegExp(alias)}\\.`),
	];
	const attributeNames = [
		`(?:${namespacePrefixes.join('|')})?FormerlySerializedAs(?:Attribute)?`,
		...aliases.attributeAliases.map(escapeRegExp),
	];
	const pattern = new RegExp(`\\b(?:${attributeNames.join('|')})\\s*\\(\\s*"${escapedName}"\\s*\\)`);

	return pattern.test(attributesText);
}

function findUsingInsertOffset(text: string, sanitizedText: string) {
	const originalLines = splitLines(text);
	const sanitizedLines = splitLines(sanitizedText);
	let insertOffset = text.charCodeAt(0) === 0xFEFF ? 1 : 0;
	let lastUsingEndOffset: number | undefined;

	for (let lineIndex = 0; lineIndex < sanitizedLines.length; lineIndex++) {
		const trimmedLine = sanitizedLines[lineIndex].text.trim();
		const originalLine = originalLines[lineIndex];
		const lineEndOffset = originalLine.offset + originalLine.text.length + originalLine.eol.length;

		if (trimmedLine === '') {
			if (lastUsingEndOffset === undefined) {
				insertOffset = lineEndOffset;
			}

			continue;
		}

		if (/^(?:global\s+)?using\s+/.test(trimmedLine)) {
			lastUsingEndOffset = lineEndOffset;
			continue;
		}

		break;
	}

	return lastUsingEndOffset ?? insertOffset;
}
