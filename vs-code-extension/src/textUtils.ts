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

// Majority-wins line-ending detection: a single stray CRLF in an otherwise
// LF file no longer flips every insertion to CRLF.
export function detectLineEnding(text: string) {
	const crlfCount = (text.match(/\r\n/g) ?? []).length;
	const lfOnlyCount = (text.match(/(?<!\r)\n/g) ?? []).length;

	return crlfCount > lfOnlyCount ? '\r\n' : '\n';
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

// ─── Source sanitizing ─────────────────────────────────────────────────────────
//
// sanitizeSource produces a same-length copy of the text where the contents of
// comments (line and block), string literals (regular, verbatim, raw,
// interpolated), character literals, and inactive #if/#elif/#else branches are
// replaced with spaces. Newlines are preserved so line and offset arithmetic on
// the sanitized text maps 1:1 onto the original text. Structural parsing runs
// on the sanitized text so that code-shaped content inside comments or strings
// can never create phantom fields or fake type declarations.

const sanitizeCacheLimit = 8;
const sanitizeCache = new Map<string, string>();

export function sanitizeSource(text: string): string {
	const cached = sanitizeCache.get(text);

	if (cached !== undefined) {
		sanitizeCache.delete(text);
		sanitizeCache.set(text, cached);
		return cached;
	}

	const sanitized = blankInactiveConditionalBlocks(blankCommentsAndStrings(text));

	sanitizeCache.set(text, sanitized);
	if (sanitizeCache.size > sanitizeCacheLimit) {
		const oldestKey = sanitizeCache.keys().next().value;
		if (oldestKey !== undefined) {
			sanitizeCache.delete(oldestKey);
		}
	}

	return sanitized;
}

function blankCommentsAndStrings(text: string): string {
	const output = text.split('');
	const length = text.length;

	const blank = (from: number, to: number) => {
		for (let index = from; index < to && index < length; index++) {
			const character = text[index];
			if (character !== '\n' && character !== '\r') {
				output[index] = ' ';
			}
		}
	};

	let index = 0;

	while (index < length) {
		const character = text[index];
		const next = index + 1 < length ? text[index + 1] : '';

		// Line comment.
		if (character === '/' && next === '/') {
			let end = index;
			while (end < length && text[end] !== '\n' && text[end] !== '\r') {
				end++;
			}
			blank(index, end);
			index = end;
			continue;
		}

		// Block comment (may span lines).
		if (character === '/' && next === '*') {
			let end = index + 2;
			while (end < length && !(text[end] === '*' && text[end + 1] === '/')) {
				end++;
			}
			end = Math.min(length, end + 2);
			blank(index, end);
			index = end;
			continue;
		}

		// Raw string literal """..."""` (C# 11), with any longer quote run as delimiter.
		if (character === '"' && next === '"' && text[index + 2] === '"') {
			let quoteEnd = index;
			while (quoteEnd < length && text[quoteEnd] === '"') {
				quoteEnd++;
			}
			const delimiterLength = quoteEnd - index;
			let end = quoteEnd;
			while (end < length) {
				if (text[end] === '"') {
					let run = end;
					while (run < length && text[run] === '"') {
						run++;
					}
					if (run - end >= delimiterLength) {
						end = run;
						break;
					}
					end = run;
					continue;
				}
				end++;
			}
			blank(index, end);
			index = end;
			continue;
		}

		// Verbatim string @"..." (and interpolated verbatim $@"..." / @$"...").
		const isVerbatimStart =
			(character === '@' && next === '"') ||
			(character === '@' && next === '$' && text[index + 2] === '"') ||
			(character === '$' && next === '@' && text[index + 2] === '"');

		if (isVerbatimStart) {
			let end = next === '"' ? index + 2 : index + 3;
			while (end < length) {
				if (text[end] === '"') {
					if (text[end + 1] === '"') {
						end += 2;
						continue;
					}
					end++;
					break;
				}
				end++;
			}
			blank(index, end);
			index = end;
			continue;
		}

		// Regular or interpolated string "..." / $"...".
		if (character === '"' || (character === '$' && next === '"')) {
			let end = character === '$' ? index + 2 : index + 1;
			while (end < length) {
				if (text[end] === '\\') {
					end += 2;
					continue;
				}
				if (text[end] === '"') {
					end++;
					break;
				}
				if (text[end] === '\n' || text[end] === '\r') {
					break;
				}
				end++;
			}
			blank(index, end);
			index = end;
			continue;
		}

		// Character literal.
		if (character === '\'') {
			let end = index + 1;
			while (end < length) {
				if (text[end] === '\\') {
					end += 2;
					continue;
				}
				if (text[end] === '\'') {
					end++;
					break;
				}
				if (text[end] === '\n' || text[end] === '\r' || end - index > 8) {
					break;
				}
				end++;
			}
			blank(index, end);
			index = end;
			continue;
		}

		index++;
	}

	return output.join('');
}

// Keeps only the first branch of every #if/#elif/#else/#endif group so
// conditionally-duplicated fields never appear twice. Directive lines
// themselves are blanked too.
function blankInactiveConditionalBlocks(text: string): string {
	const lines = splitLines(text);
	const stack: { active: boolean }[] = [];
	let output = '';

	const blankLine = (lineText: string) => ' '.repeat(lineText.length);

	for (const line of lines) {
		const directive = /^\s*#\s*(if|elif|else|endif)\b/.exec(line.text);

		if (directive) {
			const kind = directive[1];

			if (kind === 'if') {
				const parentActive = stack.every((frame) => frame.active);
				stack.push({ active: parentActive });
			} else if (kind === 'elif' || kind === 'else') {
				const frame = stack[stack.length - 1];
				if (frame) {
					frame.active = false;
				}
			} else {
				stack.pop();
			}

			output += blankLine(line.text) + line.eol;
			continue;
		}

		const active = stack.every((frame) => frame.active);
		output += (active ? line.text : blankLine(line.text)) + line.eol;
	}

	return output;
}

// ─── Identifier occurrence checks ──────────────────────────────────────────────

// True when the identifier still appears in actual code (comments, strings,
// and inactive preprocessor branches are ignored).
export function identifierOccursInCode(text: string, identifier: string): boolean {
	return countIdentifierOccurrencesInCode(text, identifier) > 0;
}

export function countIdentifierOccurrencesInCode(text: string, identifier: string): number {
	const sanitized = sanitizeSource(text);
	const pattern = new RegExp(`\\b${escapeRegExp(identifier)}\\b`, 'g');

	return (sanitized.match(pattern) ?? []).length;
}
