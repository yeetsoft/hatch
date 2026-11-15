/* The console's commands: the rows behind the `>` prefix.

   A command is a sentence for a name, a `when` that says where it is offered,
   and what it runs. What it runs is handed a `CommandEnv` rather than reaching
   for the router or the theme itself, so the tests give it a recording fake and
   ConsoleProvider the real one. Add to `CommandEnv` as a command needs more.

   Matching is on letters and digits alone, ignoring case, with every typed
   word found anywhere in the name: `board`, `go board` and `goto board` all
   reach "Go to Board". Pure and tested in node. */

import type { ThemeChoice } from '@hatch/ui';
import { HOME, HOME_LABEL, navRows } from './nav';

export interface CommandContext {
  isAdmin: boolean;
  /** The page the console was opened on. */
  pathname: string;
}

export interface CommandEnv {
  /** Opens a page, keeping the tab's scope. */
  go: (path: string) => void;
  setTheme: (choice: ThemeChoice) => void;
  /** Opens the work queue: on the board itself, or by landing on the board with it open. */
  openQueue: () => void;
  /** Asks for the New issue dialog: the board's own on the board, the
      console's elsewhere. */
  newIssue: () => void;
}

export interface Command {
  id: string;
  name: string;
  /** The short word at the row's right: what kind of thing it is. */
  hint: string;
  /** Where the command is offered; always, when absent. */
  when?: (ctx: CommandContext) => boolean;
  run: (env: CommandEnv) => void;
}

/** How many commands a bare `>` shows. */
export const COMMAND_PREVIEW = 8;

const THEMES: { choice: ThemeChoice; label: string }[] = [
  { choice: 'auto', label: 'Auto' },
  { choice: 'light', label: 'Light' },
  { choice: 'dark', label: 'Dark' },
];

function goTo(path: string, label: string): Command {
  return {
    id: `go:${path}`,
    name: `Go to ${label}`,
    hint: 'page',
    run: (env) => env.go(path),
  };
}

/** The commands whose `when` allows them here. */
export function offered(commands: Command[], ctx: CommandContext): Command[] {
  return commands.filter((command) => command.when?.(ctx) ?? true);
}

/** Every command offered here, in declared order: the pages the primary nav
    lists for this person, Board first and Settings last, then New issue, the queue and the themes. */
export function commandList(ctx: CommandContext): Command[] {
  return offered(
    [
      goTo(HOME, HOME_LABEL),
      ...navRows(ctx.isAdmin).map((row) => goTo(row.to, row.label)),
      goTo('/settings', 'Settings'),
      { id: 'new-issue', name: 'New issue', hint: 'issue', run: (env) => env.newIssue() },
      { id: 'queue', name: 'Open the work queue', hint: 'queue', run: (env) => env.openQueue() },
      ...THEMES.map(({ choice, label }): Command => ({
        id: `theme:${choice}`,
        name: `Theme → ${label}`,
        hint: 'theme',
        run: (env) => env.setTheme(choice),
      })),
    ],
    ctx,
  );
}

/** Lower-case letters and digits only. */
export function squash(text: string): string {
  return text.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
}

/** The commands a query reaches, best first, and how many there were before
    the preview cap. A bare query shows the first `COMMAND_PREVIEW` in declared
    order; a typed one is ranked - names that start with it, then any-word
    matches, then by name - and is not capped. */
export function commandRows(commands: Command[], query: string): { rows: Command[]; total: number } {
  const words = query
    .split(/[^\p{L}\p{N}]+/u)
    .map(squash)
    .filter(Boolean);
  if (words.length === 0) return { rows: commands.slice(0, COMMAND_PREVIEW), total: commands.length };

  const whole = words.join('');
  const hits = commands
    .map((command, order) => ({ command, order, name: squash(command.name) }))
    .filter(({ name }) => words.every((word) => name.includes(word)))
    .map((hit) => ({ ...hit, rank: hit.name.startsWith(whole) ? 0 : 1 }))
    .sort((a, b) => a.rank - b.rank || a.command.name.localeCompare(b.command.name) || a.order - b.order);
  return { rows: hits.map((hit) => hit.command), total: hits.length };
}

/** The status line's left half in commands mode. */
export function commandStatus(shown: number, total: number): string {
  if (total === 0) return 'no command matches';
  if (shown < total) return `${shown} of ${total} — keep typing`;
  return total === 1 ? '1 command' : `${total} commands`;
}
