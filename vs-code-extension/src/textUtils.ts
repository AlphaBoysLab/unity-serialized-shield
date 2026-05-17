export type TextLine = {
	text: string;
	eol: string;
	offset: number;
};

export type TextInsertion = {
	offset: number;
	text: string;
};

export function splitLines(text: string) {
	const lines: TextLine[] = [];
	const linePattern = /(.*?)(\r\n|\r|\n|$)/g;
	let match: RegExpExecArray | null;

	while ((match = linePattern.exec(text)) !== null) {
		if (match[0] === '') {
			break;
		}

		lines.push({
			text: match[1],
			eol: match[2],
			offset: match.index,
		});
	}

	return lines;
}

export function stripLineComment(line: string) {
	let inString = false;
	let inChar = false;
	let escaped = false;

	for (let index = 0; index < line.length - 1; index++) {
		const character = line[index];
		const nextCharacter = line[index + 1];

		if (escaped) {
			escaped = false;
			continue;
		}

		if (character === '\\' && (inString || inChar)) {
			escaped = true;
			continue;
		}

		if (character === '"' && !inChar) {
			inString = !inString;
			continue;
		}

		if (character === '\'' && !inString) {
			inChar = !inChar;
			continue;
		}

		if (!inString && !inChar && character === '/' && nextCharacter === '/') {
			return line.slice(0, index);
		}
	}

	return line;
}

export function detectLineEnding(text: string) {
	return text.includes('\r\n') ? '\r\n' : '\n';
}

export function normalizeWhitespace(text: string) {
	return text.replace(/\s+/g, ' ').trim();
}

export function escapeRegExp(text: string) {
	return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export function escapeCsharpString(text: string) {
	return text.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}
