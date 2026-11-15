/* The console's commands: the rows behind the `>` prefix.

   A command is a sentence for a name, a `when` that says where it is offered,
   and what it runs. What it runs is handed a `CommandEnv` rather than reaching
   for the router or the theme itself, so the tests give it a recording fake and
   ConsoleProvider the real one. Add to `CommandEnv` as a command needs more.

   Matching is on letters and digits alone, ignoring case, with every typed
   word found anywhere in the name: `board`, `go board` and `goto board` all
   reach "Go to Board". Pure and tested in node. */

import type { ThemeChoice } from '@hatch/ui';
import { DEFAULT_FILTER, isFiltering, toggleBlocked, toggleWaiting } from './filter';
import type { CardFilter } from './filter';
import { HOME, HOME_LABEL, navRows } from './nav';
import { PERMISSION_TITLE, PRE_APPROVAL_LEVELS, approvalLocked } from './preApproval';
import type { PreApproval } from './preApproval';
import { PRIORITY_LEVELS } from './priority';
import type { Priority } from './priority';
import { moveBlocked } from './protectedColumns';
import { hasMultipleProjects } from './useProjects';
import type { IssueCard, Project, Status } from '../types';

/** What the board lends the console to set its filter: the filter as drawn, the
    projects its picker offers, and the board's own `changeFilter`. */
export interface FilterHandle {
  filter: CardFilter;
  projects: Project[];
  onChange: (next: CardFilter) => void;
}

/** The issue the person is looking at, and what is needed to say where it may
    go. Absent from the context where no issue is in view - see
    lib/issueInView.ts. */
export interface IssueView {
  card: IssueCard;
  statuses: Status[];
  /** The issue's own project, or null where it is not in the caller's list -
      which `moveBlocked` reads as not an approver. */
  project: Project | null;
}

export interface CommandContext {
  isAdmin: boolean;
  /** The page the console was opened on. */
  pathname: string;
  /** The board's filter; absent off the board, which offers no filter rows. */
  filters?: FilterHandle;
  /** The issue in view, or null. */
  view: IssueView | null;
  /** Whether the caller may set an issue's priority and pre-approval: the
      controls draw a word, not a button, for anyone but a person. */
  canSet: boolean;
}

/** The second lists a command can open. */
export type CommandStep = 'status' | 'priority' | 'preapproval';

export interface CommandEnv {
  /** Opens a page, keeping the tab's scope. */
  go: (path: string) => void;
  setTheme: (choice: ThemeChoice) => void;
  /** Sets the board's filter, through the board's own `changeFilter`. */
  changeFilter: (next: CardFilter) => void;
  /** Moves the issue in view to a column, as the status bar would. */
  move: (statusId: number) => void;
  /** Sets the issue in view's priority, as the priority band would. */
  setPriority: (level: Priority) => void;
  /** Sets the issue in view's pre-approval, as the pre-approval band would. */
  setPreApproval: (level: PreApproval) => void;
  /** Swaps the list for a second one. */
  openStep: (step: CommandStep) => void;
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
  /** Taking it does not close the console: it opens a step, or moves an issue
      and may then ask about what is under it. */
  stays?: boolean;
  /** Why the command cannot be taken here, as a sentence. A row that has one is
      listed and never run. */
  disabled?: string;
  /** The list this command opens. */
  step?: CommandStep;
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

const onOff = (on: boolean) => (on ? 'on' : 'off');

/** The board-only rows, `[]` off the board. Each hands the board a whole filter
    through `changeFilter`, so `?project=` and the browser's memory follow as
    they do from the filter row. */
export function filterCommands(filters?: FilterHandle): Command[] {
  if (!filters) return [];
  const { filter, projects } = filters;

  const commands: Command[] = [
    {
      id: 'filter:blocked',
      name: 'Filter → Blocked',
      hint: onOff(filter.blocked),
      run: (env) => env.changeFilter(toggleBlocked(filter)),
    },
    {
      id: 'filter:waiting',
      name: 'Filter → Needs me',
      hint: onOff(filter.waiting),
      run: (env) => env.changeFilter(toggleWaiting(filter)),
    },
  ];
  /* One row per project, drawn only when there is a choice, as the picker is,
     and named with the picker's own option text. */
  if (hasMultipleProjects(projects)) {
    for (const { key, name } of projects) {
      commands.push({
        id: `filter:project:${key}`,
        name: `Filter → ${key} — ${name}`,
        hint: filter.project === key ? 'on' : 'project',
        run: (env) => env.changeFilter({ ...filter, project: key }),
      });
    }
  }
  if (isFiltering(filter)) {
    commands.push({
      id: 'filter:clear',
      name: 'Clear filters',
      hint: 'filter',
      run: (env) => env.changeFilter({ ...DEFAULT_FILTER }),
    });
  }
  return commands;
}

/** The commands whose `when` allows them here. */
export function offered(commands: Command[], ctx: CommandContext): Command[] {
  return commands.filter((command) => command.when?.(ctx) ?? true);
}

/** A column's row. Built from the columns the board sent, so nothing here
    names one. The current column is listed and not takeable; one the server
    would refuse says why in `moveBlocked`'s own words. */
function statusRow(view: IssueView, status: Status, name: string, id: string): Command {
  const current = status.id === view.card.statusId;
  const blocked = current ? null : moveBlocked(status, view.project, view.card);
  return {
    id,
    name,
    hint: current ? 'current' : blocked ? 'blocked' : 'status',
    stays: true,
    disabled: current ? `Already in ${status.name}` : (blocked ?? undefined),
    run: (env) => env.move(status.id),
  };
}

/** The status step's rows: every column, deferred ones too, by name alone. */
export function statusStepRows(view: IssueView): Command[] {
  return view.statuses.map((status) => statusRow(view, status, status.name, `step:status:${status.id}`));
}

/** A priority level's row. The issue's own level is listed and not takeable. */
function priorityRow(view: IssueView, level: (typeof PRIORITY_LEVELS)[number], name: string, id: string): Command {
  const current = level.name === view.card.priorityOwn;
  return {
    id,
    name,
    hint: current ? 'current' : 'priority',
    stays: true,
    disabled: current ? `Already ${level.word}` : undefined,
    run: (env) => env.setPriority(level.name),
  };
}

/** A pre-approval level's row: the own level is listed and not takeable, and
    one the server would refuse says why in the band's own sentence. Current
    wins over blocked, as in `statusRow`. A project the caller does not have
    reads as not an approver. */
function preApprovalRow(view: IssueView, level: (typeof PRE_APPROVAL_LEVELS)[number], name: string, id: string): Command {
  const own = view.card.preApprovalOwn;
  const current = level.name === own;
  const blocked = approvalLocked(level.name, own, view.project?.canApprove ?? false);
  return {
    id,
    name,
    hint: current ? 'current' : blocked ? 'blocked' : 'pre-approval',
    stays: true,
    disabled: current ? `Already ${level.word}` : blocked ? PERMISSION_TITLE : undefined,
    run: (env) => env.setPreApproval(level.name),
  };
}

/** The priority step's rows: every level, in the band's order, by word alone. */
export function priorityStepRows(view: IssueView): Command[] {
  return PRIORITY_LEVELS.map((level) => priorityRow(view, level, level.word, `step:priority:${level.name}`));
}

/** The pre-approval step's rows: every level, in the band's order, by word alone. */
export function preApprovalStepRows(view: IssueView): Command[] {
  return PRE_APPROVAL_LEVELS.map((level) => preApprovalRow(view, level, level.word, `step:preapproval:${level.name}`));
}

/** Every command offered here, in declared order: Status where an issue is in
    view, then Priority and Pre-approval where the caller is a person, then the pages the primary nav lists for this person, Board first and
    Settings last, then New issue, the queue and the themes, then the board's filter
    rows when there is a board, then a `Status → <Column>` for each column, and a
    `Priority → <Level>` and `Pre-approval → <Level>` for each level. Those come
    last so the bare preview is not a screenful of them. */
export function commandList(ctx: CommandContext): Command[] {
  const { view } = ctx;
  const setting = view !== null && ctx.canSet;
  return offered(
    [
      {
        id: 'status',
        name: 'Status',
        hint: 'issue',
        when: (c) => c.view !== null,
        stays: true,
        step: 'status',
        run: (env) => env.openStep('status'),
      },
      {
        id: 'priority',
        name: 'Priority',
        hint: 'issue',
        when: (c) => c.view !== null && c.canSet,
        stays: true,
        step: 'priority',
        run: (env) => env.openStep('priority'),
      },
      {
        id: 'preapproval',
        name: 'Pre-approval',
        hint: 'issue',
        when: (c) => c.view !== null && c.canSet,
        stays: true,
        step: 'preapproval',
        run: (env) => env.openStep('preapproval'),
      },
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
      ...filterCommands(ctx.filters),
      ...(view
        ? view.statuses.map((status) => statusRow(view, status, `Status → ${status.name}`, `status:${status.id}`))
        : []),
      ...(view && setting
        ? [
            ...PRIORITY_LEVELS.map((level) =>
              priorityRow(view, level, `Priority → ${level.word}`, `priority:${level.name}`),
            ),
            ...PRE_APPROVAL_LEVELS.map((level) =>
              preApprovalRow(view, level, `Pre-approval → ${level.word}`, `preapproval:${level.name}`),
            ),
          ]
        : []),
    ],
    ctx,
  );
}

/** Lower-case letters and digits only. */
export function squash(text: string): string {
  return text.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
}

/** The commands a query reaches, best first, and how many there were before
    the preview cap. A bare query shows the first `preview` in declared order; a
    typed one is ranked and not capped. Three ranks: a name that starts with the
    query; a name with a ` → ` part that does, so `todo` finds `Status → To Do`
    before `Go to Docs`, whose letters only contain it; then any-word matches.
    Within a rank, by name. */
export function commandRows(
  commands: Command[],
  query: string,
  preview = COMMAND_PREVIEW,
): { rows: Command[]; total: number } {
  const words = query
    .split(/[^\p{L}\p{N}]+/u)
    .map(squash)
    .filter(Boolean);
  if (words.length === 0) return { rows: commands.slice(0, preview), total: commands.length };

  const whole = words.join('');
  const hits = commands
    .map((command, order) => ({
      command,
      order,
      name: squash(command.name),
      parts: command.name.split(' → ').map(squash),
    }))
    .filter(({ name }) => words.every((word) => name.includes(word)))
    .map((hit) => ({
      ...hit,
      rank: hit.name.startsWith(whole) ? 0 : hit.parts.some((part) => part.startsWith(whole)) ? 1 : 2,
    }))
    .sort((a, b) => a.rank - b.rank || a.command.name.localeCompare(b.command.name) || a.order - b.order);
  return { rows: hits.map((hit) => hit.command), total: hits.length };
}

/** The status line's left half in commands mode. */
export function commandStatus(shown: number, total: number): string {
  if (total === 0) return 'no command matches';
  if (shown < total) return `${shown} of ${total} — keep typing`;
  return total === 1 ? '1 command' : `${total} commands`;
}
