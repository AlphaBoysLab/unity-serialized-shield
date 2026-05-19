import { parseSerializedFields, SerializedField } from './serializedFieldParser';
import {
	detectLineEnding,
	escapeCsharpString,
	escapeRegExp,
	splitLines,
	stripLineComment,
	TextInsertion,
} from './textUtils';

export type { TextInsertion } from './textUtils';

const serializationUsing = 'using UnityEngine.Serialization;';
const serializationUsingPattern = /\b(?:global\s+)?using\s+UnityEngine\.Serialization\s*;/;

export type SerializedFieldRename = {
	previousName: string;
	previousSerializedName: string;
	currentName: string;
	currentSerializedName: string;
	currentField: SerializedField;
};

export function buildFormerlySerializedAsEdits(previousText: string, currentText: string): TextInsertion[] {
	const renames = findRenamedSerializedFields(previousText, currentText);
	const eol = detectLineEnding(currentText);
	const insertions: TextInsertion[] = [];

	for (const rename of renames) {
		if (hasFormerlySerializedAs(rename.currentField.attributesText, rename.previousSerializedName)) {
			continue;
		}

		insertions.push({
			offset: rename.currentField.insertOffset,
			text: `${rename.currentField.indent}[FormerlySerializedAs("${escapeCsharpString(rename.previousSerializedName)}")]${eol}`,
		});
	}

	if (insertions.length > 0 && !serializationUsingPattern.test(currentText)) {
		insertions.unshift({
			offset: findUsingInsertOffset(currentText),
			text: `${serializationUsing}${eol}`,
		});
	}

	return insertions;
}

export function findRenamedSerializedFields(previousText: string, currentText: string): SerializedFieldRename[] {
	const previousFields = uniqueFieldsByKey(parseSerializedFields(previousText));
	const currentFields = uniqueFieldsByKey(parseSerializedFields(currentText));
	const renames: SerializedFieldRename[] = [];

	for (const [key, previousField] of previousFields) {
		const currentField = currentFields.get(key);

		if (!currentField || currentField.name === previousField.name) {
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

	return renames;
}

function uniqueFieldsByKey(fields: SerializedField[]) {
	const groupedFields = new Map<string, SerializedField[]>();

	for (const field of fields) {
		const existingFields = groupedFields.get(field.key) ?? [];
		existingFields.push(field);
		groupedFields.set(field.key, existingFields);
	}

	const uniqueFields = new Map<string, SerializedField>();

	for (const [key, fieldsForKey] of groupedFields) {
		if (fieldsForKey.length === 1) {
			uniqueFields.set(key, fieldsForKey[0]);
		}
	}

	return uniqueFields;
}

function hasFormerlySerializedAs(attributesText: string, previousName: string) {
	const escapedName = escapeRegExp(previousName);
	const pattern = new RegExp(`\\b(?:UnityEngine\\.Serialization\\.)?FormerlySerializedAs(?:Attribute)?\\s*\\(\\s*"${escapedName}"\\s*\\)`);

	return pattern.test(attributesText);
}

function findUsingInsertOffset(text: string) {
	const lines = splitLines(text);
	let insertOffset = text.charCodeAt(0) === 0xFEFF ? 1 : 0;
	let lastUsingEndOffset: number | undefined;

	for (const line of lines) {
		const trimmedLine = stripLineComment(line.text).trim();
		const lineEndOffset = line.offset + line.text.length + line.eol.length;

		if (trimmedLine === '' || trimmedLine.startsWith('//')) {
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
