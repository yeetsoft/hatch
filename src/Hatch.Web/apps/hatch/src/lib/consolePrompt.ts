/* What the console's prompt says it is asking for.

   The first character picks the mode: `/` issues, `>` commands, `?` help, and
   no prefix is the issue search the console has always been. `/` is a prefix
   as well as the key that opens the console - the opening keystroke is
   swallowed, so a `/` in the prompt only says "issues" again. Pure and
   tested in node; OmniBar only draws what this returns. */

export type ConsoleMode = 'issues' | 'commands' | 'help';

export interface ParsedPrompt {
  mode: ConsoleMode;
  /** The prompt without its prefix. Empty for help, whatever follows the `?`. */
  query: string;
}

export interface PrefixRow {
  prefix: '/' | '>' | '?';
  mode: ConsoleMode;
  line: string;
}

/** The prefixes and what each one means, in the order help lists them. */
export const PREFIXES: PrefixRow[] = [
  {
    prefix: '/',
    mode: 'issues',
    line: 'issues: a key, a number, or words from a title',
  },
  { prefix: '>', mode: 'commands', line: 'commands: pages and themes' },
  { prefix: '?', mode: 'help', line: 'this list' },
];

/** The prompt's text as a mode and the rest of it. A leading space is ignored
    before the prefix is read. */
export function parsePrompt(text: string): ParsedPrompt {
  const trimmed = text.trimStart();
  const row = PREFIXES.find((p) => trimmed.startsWith(p.prefix));
  if (!row) return { mode: 'issues', query: text };
  return { mode: row.mode, query: row.mode === 'help' ? '' : trimmed.slice(1) };
}

/** The prompt's verb, which says what Enter will do. */
export const MODE_VERB: Record<ConsoleMode, string> = {
  issues: 'go',
  commands: 'run',
  help: 'help',
};

/** The accessible name of the dialog, the prompt and the list. */
export const MODE_LABEL: Record<ConsoleMode, string> = {
  issues: 'Go to an issue',
  commands: 'Run a command',
  help: 'Console help',
};
