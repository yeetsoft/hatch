import { describe, expect, it } from 'vitest';
import type { ThemeChoice } from '@hatch/ui';
import {
  COMMAND_PREVIEW,
  commandList,
  commandRows,
  commandStatus,
  filterCommands,
  offered,
  preApprovalStepRows,
  priorityStepRows,
  squash,
  statusStepRows,
} from './consoleCommands';
import type { Command, CommandContext, CommandEnv, FilterHandle, IssueView } from './consoleCommands';
import { DEFAULT_FILTER } from './filter';
import type { CardFilter } from './filter';
import { PERMISSION_TITLE, PRE_APPROVAL_LEVELS } from './preApproval';
import type { PreApproval } from './preApproval';
import { PRIORITY_LEVELS } from './priority';
import type { Priority } from './priority';
import { moveBlocked } from './protectedColumns';
import { navRows } from './nav';
import type { IssueCard, Project, Status } from '../types';

const admin: CommandContext = { isAdmin: true, pathname: '/', view: null, canSet: false };
const person: CommandContext = { isAdmin: false, pathname: '/', view: null, canSet: false };

const names = (commands: Command[]) => commands.map((c) => c.name);
const find = (commands: Command[], name: string) => commands.find((c) => c.name === name)!;

/* A recording fake for what a command runs against. */
function env() {
  const went: string[] = [];
  const themes: ThemeChoice[] = [];
  const filters: CardFilter[] = [];
  const moved: number[] = [];
  const priorities: Priority[] = [];
  const approvals: PreApproval[] = [];
  const steps: string[] = [];
  const filed = { count: 0 };
  const queue = { opened: 0 };
  const fake: CommandEnv = {
    go: (p) => went.push(p),
    setTheme: (c) => themes.push(c),
    changeFilter: (next) => filters.push(next),
    move: (id) => moved.push(id),
    setPriority: (level) => priorities.push(level),
    setPreApproval: (level) => approvals.push(level),
    openStep: (step) => steps.push(step),
    newIssue: () => filed.count++,
    openQueue: () => queue.opened++,
  };
  return { fake, went, themes, filters, moved, priorities, approvals, steps, filed, queue };
}

const made = (name: string, over: Partial<Command> = {}): Command => ({
  id: name,
  name,
  hint: 'test',
  run: () => {},
  ...over,
});

describe('squash', () => {
  it('keeps lower-case letters and digits', () => {
    expect(squash('Go-to  BOARD!')).toBe('gotoboard');
    expect(squash('Theme → Dark')).toBe('themedark');
  });
});

describe('commandList', () => {
  it('opens on Board, then the nav rows, then Settings, then New issue, then the queue, then the themes', () => {
    const list = names(commandList(admin));
    expect(list[0]).toBe('Go to Board');
    expect(list[1]).toBe('Go to Search');
    expect(list.slice(-6)).toEqual([
      'Go to Settings',
      'New issue',
      'Open the work queue',
      'Theme → Auto',
      'Theme → Light',
      'Theme → Dark',
    ]);
  });

  it('has a Go to for every row the nav lists, in its order', () => {
    for (const ctx of [admin, person]) {
      const list = commandList(ctx);
      const goes = list.filter((c) => c.id.startsWith('go:') && c.id !== 'go:/' && c.id !== 'go:/settings');
      expect(goes.map((c) => c.id)).toEqual(navRows(ctx.isAdmin).map((r) => `go:${r.to}`));
      expect(goes.map((c) => c.name)).toEqual(navRows(ctx.isAdmin).map((r) => `Go to ${r.label}`));
    }
  });

  it('offers Go to Users to an admin and to nobody else', () => {
    expect(names(commandList(admin))).toContain('Go to Users');
    expect(names(commandList(person))).not.toContain('Go to Users');
  });

  it('runs a go row with its path', () => {
    const { fake, went } = env();
    find(commandList(admin), 'Go to Board').run(fake);
    find(commandList(admin), 'Go to Plan').run(fake);
    find(commandList(admin), 'Go to Settings').run(fake);
    expect(went).toEqual(['/', '/plan', '/settings']);
  });

  it('offers New issue to everybody on every page, and runs it by asking for the dialog', () => {
    for (const ctx of [admin, person, { ...person, pathname: '/search' }]) {
      const { fake, went, themes, filed } = env();
      find(commandList(ctx), 'New issue').run(fake);
      expect(filed.count).toBe(1);
      expect(went).toEqual([]);
      expect(themes).toEqual([]);
    }
  });

  it('offers Open the work queue to an admin and to everybody else', () => {
    expect(names(commandList(admin))).toContain('Open the work queue');
    expect(names(commandList(person))).toContain('Open the work queue');
  });

  it('runs Open the work queue once, and nothing else', () => {
    const { fake, went, themes, queue } = env();
    find(commandList(person), 'Open the work queue').run(fake);
    expect(queue.opened).toBe(1);
    expect(went).toEqual([]);
    expect(themes).toEqual([]);
  });

  it('runs each theme with its choice', () => {
    const { fake, themes } = env();
    for (const label of ['Auto', 'Light', 'Dark']) find(commandList(person), `Theme → ${label}`).run(fake);
    expect(themes).toEqual(['auto', 'light', 'dark']);
  });
});

describe('commandRows', () => {
  const all = commandList(admin);

  it.each(['go board', 'board', 'goto board', 'GO-BOARD', 'Board go'])('reaches Go to Board for %j', (query) => {
    expect(commandRows(all, query).rows[0].name).toBe('Go to Board');
  });

  it('lists the same row for board, go board and goto board', () => {
    const board = names(commandRows(all, 'board').rows);
    expect(names(commandRows(all, 'go board').rows)).toEqual(board);
    expect(names(commandRows(all, 'goto board').rows)).toEqual(board);
  });

  it.each(['new', 'issue', 'new issue'])('reaches New issue for %j', (query) => {
    expect(commandRows(all, query).rows[0].name).toBe('New issue');
  });

  it('reaches a theme by its choice', () => {
    expect(names(commandRows(all, 'dark').rows)).toEqual(['Theme → Dark']);
    expect(names(commandRows(all, 'auto').rows)).toEqual(['Theme → Auto']);
    expect(names(commandRows(all, 'light').rows)).toEqual(['Theme → Light']);
  });

  it.each(['queue', 'work', 'Work Queue', 'open queue'])('reaches only Open the work queue for %j', (query) => {
    expect(names(commandRows(all, query).rows)).toEqual(['Open the work queue']);
  });

  it('finds Users for an admin and nothing for anybody else', () => {
    expect(names(commandRows(all, 'users').rows)).toEqual(['Go to Users']);
    expect(commandRows(commandList(person), 'users')).toEqual({
      rows: [],
      total: 0,
    });
  });

  it('puts names that start with the query before any-word matches', () => {
    const list = [made('Open the theme'), made('Theme → Dark'), made('Dark mode, theme')];
    expect(names(commandRows(list, 'theme').rows)).toEqual(['Theme → Dark', 'Dark mode, theme', 'Open the theme']);
  });

  it('matches any word in any order, and every word must match', () => {
    const list = [made('Go to Board'), made('Go to Plan')];
    expect(names(commandRows(list, 'board go').rows)).toEqual(['Go to Board']);
    expect(commandRows(list, 'board plan').total).toBe(0);
  });

  it('breaks ties by name, then by declared order', () => {
    const list = [made('Go to Plan'), made('Go to Dreams'), made('Go to Dreams', { id: 'again' })];
    expect(commandRows(list, 'go').rows.map((c) => c.id)).toEqual(['Go to Dreams', 'again', 'Go to Plan']);
  });

  it('shows the first eight in declared order for an empty query', () => {
    const { rows, total } = commandRows(all, '');
    expect(rows).toHaveLength(COMMAND_PREVIEW);
    expect(names(rows)).toEqual(names(all).slice(0, COMMAND_PREVIEW));
    expect(total).toBe(all.length);
    expect(names(rows)).toEqual([
      'Go to Board',
      'Go to Search',
      'Go to Ballot',
      'Go to Ambitions',
      'Go to Dreams',
      'Go to Plan',
      'Go to Reports',
      'Go to History',
    ]);
  });

  it('treats a query with no letters or digits as empty', () => {
    expect(commandRows(all, ' - ').rows).toHaveLength(COMMAND_PREVIEW);
  });

  it('does not cap a typed query', () => {
    const { rows, total } = commandRows(all, 'go');
    expect(rows.length).toBe(total);
    expect(total).toBeGreaterThan(COMMAND_PREVIEW);
  });

  it('hides a command off its page with `when`', () => {
    const onPlan = made('Do a thing', {
      when: (ctx) => ctx.pathname === '/plan',
    });
    expect(offered([onPlan], { ...admin, pathname: '/plan' })).toEqual([onPlan]);
    expect(offered([onPlan], admin)).toEqual([]);
  });
});

describe('commandStatus', () => {
  it('says how many of how many while capped', () => {
    expect(commandStatus(8, 21)).toBe('8 of 21 — keep typing');
  });

  it('says none, one and many', () => {
    expect(commandStatus(0, 0)).toBe('no command matches');
    expect(commandStatus(1, 1)).toBe('1 command');
    expect(commandStatus(17, 17)).toBe('17 commands');
  });
});

describe('filterCommands', () => {
  const two = [
    { key: 'HA', name: 'Hatch' },
    { key: 'AER', name: 'Aerie' },
  ] as Project[];
  const one = [{ key: 'HA', name: 'Hatch' }] as Project[];
  const handle = (over: Partial<CardFilter> = {}, projects: Project[] = one) => {
    const given: CardFilter[] = [];
    const filters: FilterHandle = {
      filter: { ...DEFAULT_FILTER, ...over },
      projects,
      onChange: (next) => given.push(next),
    };
    return { filters, given };
  };

  it('offers nothing off the board', () => {
    expect(filterCommands(undefined)).toEqual([]);
    expect(names(commandList(admin))).not.toContain('Filter → Blocked');
  });

  it('offers Blocked and Needs me alone on a one-project board', () => {
    expect(names(filterCommands(handle().filters))).toEqual(['Filter → Blocked', 'Filter → Needs me']);
  });

  it('comes after the themes in the global list', () => {
    const list = names(commandList({ ...admin, filters: handle().filters }));
    expect(list.slice(-3)).toEqual(['Theme → Dark', 'Filter → Blocked', 'Filter → Needs me']);
  });

  it('reads on or off from the filter, not from the last take', () => {
    const off = filterCommands(handle().filters);
    expect(off.map((c) => c.hint)).toEqual(['off', 'off']);
    const on = filterCommands(handle({ blocked: true, waiting: true }).filters);
    expect(on.slice(0, 2).map((c) => c.hint)).toEqual(['on', 'on']);
  });

  it('hands over the flipped filter, and flips back from the result', () => {
    const { fake, filters: seen } = env();
    const { filters, given } = handle();
    find(filterCommands(filters), 'Filter → Blocked').run(fake);
    find(filterCommands(filters), 'Filter → Needs me').run(fake);
    expect(seen).toEqual([
      { ...DEFAULT_FILTER, blocked: true },
      { ...DEFAULT_FILTER, waiting: true },
    ]);
    expect(given).toEqual([]);

    for (const flipped of seen.splice(0)) {
      const { filters: back } = handle(flipped);
      const row = filterCommands(back).find((c) => c.hint === 'on')!;
      row.run(fake);
    }
    expect(seen).toEqual([DEFAULT_FILTER, DEFAULT_FILTER]);
  });

  it('offers a row per project when there are several, named as the picker is', () => {
    const rows = filterCommands(handle({}, two).filters);
    expect(names(rows)).toEqual(['Filter → Blocked', 'Filter → Needs me', 'Filter → HA — Hatch', 'Filter → AER — Aerie']);
    expect(rows.map((c) => c.id).slice(2)).toEqual(['filter:project:HA', 'filter:project:AER']);
  });

  it('sets the project when its row runs, and reads on for the current one', () => {
    const { fake, filters: seen } = env();
    const rows = filterCommands(handle({ project: 'AER' }, two).filters);
    expect(rows.slice(2, 4).map((c) => c.hint)).toEqual(['project', 'on']);
    find(rows, 'Filter → HA — Hatch').run(fake);
    expect(seen).toEqual([{ ...DEFAULT_FILTER, project: 'HA' }]);
  });

  it('offers Clear filters only while a filter is set', () => {
    expect(names(filterCommands(handle().filters))).not.toContain('Clear filters');
    const set: Partial<CardFilter>[] = [
      { blocked: true },
      { waiting: true },
      { project: 'HA' },
      { types: [] },
      { query: 'x' },
    ];
    for (const over of set) {
      expect(names(filterCommands(handle(over, two).filters)).at(-1)).toBe('Clear filters');
    }
  });

  it('returns every field to its default when Clear filters runs', () => {
    const { fake, filters: seen } = env();
    const { filters } = handle({ blocked: true, waiting: true, project: 'HA', types: [] }, two);
    find(filterCommands(filters), 'Clear filters').run(fake);
    expect(seen).toEqual([DEFAULT_FILTER]);
  });

  it('is reached by its words', () => {
    const all = commandList({ ...admin, filters: handle({ blocked: true }, two).filters });
    expect(commandRows(all, 'blocked').rows[0].name).toBe('Filter → Blocked');
    expect(commandRows(all, 'needs me').rows[0].name).toBe('Filter → Needs me');
    expect(commandRows(all, 'filter ha').rows[0].name).toBe('Filter → HA — Hatch');
    expect(names(commandRows(all, 'clear').rows)).toEqual(['Clear filters']);
  });
});

/* An issue in view, in a board with a protected column, a merging one and a
   deferred one. The column names are the test's own; the code names none. */
const column = (id: number, name: string, over: Partial<Status> = {}): Status =>
  ({
    id,
    name,
    sortOrder: id,
    isTerminal: false,
    isDeferred: false,
    isProtected: false,
    mergesPullRequest: false,
    ...over,
  }) as Status;

const COLUMNS: Status[] = [
  column(1, 'To Do'),
  column(2, 'In Progress'),
  column(3, 'In Review', { isProtected: true }),
  column(4, 'Merging', { mergesPullRequest: true }),
  column(5, 'Done', { isTerminal: true }),
  column(6, 'Parked', { isDeferred: true }),
];

const issue = (over: Partial<IssueCard> = {}) =>
  ({ key: 'AER-1', projectKey: 'AER', statusId: 2, pullRequestUrl: null, ...over }) as IssueCard;
const project = (canApprove: boolean) => ({ key: 'AER', canApprove }) as Project;
const viewOf = (over: Partial<IssueView> = {}): IssueView => ({
  card: issue(),
  statuses: COLUMNS,
  project: project(false),
  ...over,
});
const here = (view: IssueView | null): CommandContext => ({ ...person, view });

describe('the Status commands', () => {
  it('are not offered with no issue in view', () => {
    expect(names(commandList(here(null))).filter((n) => n.startsWith('Status'))).toEqual([]);
  });

  it('open with Status, so a bare > offers it first', () => {
    const list = commandList(here(viewOf()));
    expect(list[0].name).toBe('Status');
    expect(names(commandRows(list, '').rows)[0]).toBe('Status');
    expect(list[0]).toMatchObject({ stays: true, step: 'status' });
  });

  it('list one flat row per column in board order, deferred included, after everything else', () => {
    const list = commandList(here(viewOf()));
    const rows = list.filter((c) => c.name.startsWith('Status → '));
    expect(names(rows)).toEqual(COLUMNS.map((c) => `Status → ${c.name}`));
    expect(list.slice(-COLUMNS.length)).toEqual(rows);
    // So the eight-row preview is pages, not a screenful of columns.
    expect(names(commandRows(list, '').rows).some((n) => n.startsWith('Status → '))).toBe(false);
  });

  it('mark the current column and never run it', () => {
    const row = find(commandList(here(viewOf())), 'Status → In Progress');
    expect(row).toMatchObject({ hint: 'current', disabled: 'Already in In Progress' });
  });

  it('disable a protected column with the sentence moveBlocked gives a non-approver', () => {
    const view = viewOf();
    const row = find(commandList(here(view)), 'Status → In Review');
    expect(row.disabled).toBe(moveBlocked(COLUMNS[2], view.project, view.card));
    expect(row.disabled).toMatch(/approver/i);
    expect(row.hint).toBe('blocked');
  });

  it('enable a protected column for an approver', () => {
    const row = find(commandList(here(viewOf({ project: project(true) }))), 'Status → In Review');
    expect(row.disabled).toBeUndefined();
  });

  it('fail closed where the issue project is not known', () => {
    const row = find(commandList(here(viewOf({ project: null }))), 'Status → In Review');
    expect(row.disabled).toMatch(/approver/i);
  });

  it('disable a merging column for an issue with no pull request, and not with one', () => {
    const without = find(commandList(here(viewOf())), 'Status → Merging');
    expect(without.disabled).toMatch(/pull request/i);
    const withPr = find(
      commandList(here(viewOf({ card: issue({ pullRequestUrl: 'https://example.com/pr/1' }) }))),
      'Status → Merging',
    );
    expect(withPr.disabled).toBeUndefined();
  });

  it('move to the column they name, and Status opens the step', () => {
    const { fake, moved, steps } = env();
    const list = commandList(here(viewOf()));
    find(list, 'Status → To Do').run(fake);
    find(list, 'Status → Parked').run(fake);
    find(list, 'Status').run(fake);
    expect(moved).toEqual([1, 6]);
    expect(steps).toEqual(['status']);
  });

  it('give the step the same rows named by column alone', () => {
    const view = viewOf();
    const rows = statusStepRows(view);
    expect(names(rows)).toEqual(COLUMNS.map((c) => c.name));
    const flat = commandList(here(view)).filter((c) => c.name.startsWith('Status → '));
    expect(rows.map((r) => [r.hint, r.disabled])).toEqual(flat.map((r) => [r.hint, r.disabled]));
    expect(names(commandRows(rows, 'done', Infinity).rows)).toEqual(['Done']);
  });

  it('show every column in the step, with no preview cap', () => {
    const many = Array.from({ length: 12 }, (_, i) => column(i + 1, `Column ${i + 1}`));
    expect(commandRows(statusStepRows(viewOf({ statuses: many })), '', Infinity).rows).toHaveLength(12);
  });
});

describe('ranking with Status', () => {
  const list = commandList(here(viewOf()));

  it('puts Status → To Do before Go to Docs for todo', () => {
    // squash('Go to Docs') contains 'todo'; that must not outrank a column.
    const found = names(commandRows(list, 'todo').rows);
    expect(found.indexOf('Status → To Do')).toBeGreaterThanOrEqual(0);
    expect(found.indexOf('Status → To Do')).toBeLessThan(found.indexOf('Go to Docs'));
    expect(found[0]).toBe('Status → To Do');
  });

  it('finds Status → Done first for done', () => {
    expect(names(commandRows(list, 'done').rows)[0]).toBe('Status → Done');
  });

  it('puts the opener first for status', () => {
    const found = names(commandRows(list, 'status').rows);
    expect(found[0]).toBe('Status');
    // Then the columns, ahead of any page that merely mentions status.
    expect(found.slice(1, 1 + COLUMNS.length).sort()).toEqual(COLUMNS.map((c) => `Status → ${c.name}`).sort());
  });

  it('ranks a part after → that starts with the query between a name that does and any word', () => {
    const items = [made('Open the theme'), made('Theme → Dark'), made('Dark mode, theme'), made('Mode → Themes')];
    expect(names(commandRows(items, 'theme').rows)).toEqual([
      'Theme → Dark',
      'Mode → Themes',
      'Dark mode, theme',
      'Open the theme',
    ]);
  });
});

/* The two level steps. Their words and order are the controls' own, read here
   from the shared tables, so the test names no level the code does not. */
const levelled = (over: Partial<IssueCard> = {}, inProject: Project | null = project(false)): CommandContext => ({
  ...person,
  canSet: true,
  view: viewOf({ card: issue({ priorityOwn: 'normal', preApprovalOwn: 'none', ...over }), project: inProject }),
});
const isLevelRow = (name: string) => name.startsWith('Priority') || name.startsWith('Pre-approval');

describe('the Priority and Pre-approval commands', () => {
  it('are not offered with no issue in view', () => {
    expect(names(commandList({ ...person, canSet: true, view: null })).filter(isLevelRow)).toEqual([]);
  });

  it('are not offered to a caller who is not a person', () => {
    expect(names(commandList({ ...levelled(), canSet: false })).filter(isLevelRow)).toEqual([]);
  });

  it('open with their own rows, right after Status', () => {
    const list = commandList(levelled());
    expect(names(list).slice(0, 3)).toEqual(['Status', 'Priority', 'Pre-approval']);
    expect(find(list, 'Priority')).toMatchObject({ hint: 'issue', stays: true, step: 'priority' });
    expect(find(list, 'Pre-approval')).toMatchObject({ hint: 'issue', stays: true, step: 'preapproval' });
  });

  it('list a flat row per level in the controls order, after the Status rows', () => {
    const list = commandList(levelled());
    const flat = names(list.filter((c) => c.name.includes(' → ') && !c.name.startsWith('Theme')));
    expect(flat).toEqual([
      ...COLUMNS.map((c) => `Status → ${c.name}`),
      ...PRIORITY_LEVELS.map((l) => `Priority → ${l.word}`),
      ...PRE_APPROVAL_LEVELS.map((l) => `Pre-approval → ${l.word}`),
    ]);
    expect(list.map((c) => c.id)).toEqual(expect.arrayContaining(['priority:paused', 'preapproval:express']));
  });

  it('keep the flat rows out of the bare preview', () => {
    const shown = names(commandRows(commandList(levelled()), '').rows);
    expect(shown.some((n) => n.includes(' → ') && !n.startsWith('Theme'))).toBe(false);
  });

  it('give each step the controls order, by word alone', () => {
    const view = levelled().view!;
    expect(names(priorityStepRows(view))).toEqual(PRIORITY_LEVELS.map((l) => l.word));
    expect(names(preApprovalStepRows(view))).toEqual(PRE_APPROVAL_LEVELS.map((l) => l.word));
    expect(priorityStepRows(view)[0].id).toBe('step:priority:emergency');
    expect(preApprovalStepRows(view)[0].id).toBe('step:preapproval:none');
  });

  it('mark the own level current and never run it', () => {
    const view = levelled({ priorityOwn: 'expedited', preApprovalOwn: 'express' }).view!;
    expect(find(priorityStepRows(view), 'Expedited')).toMatchObject({ hint: 'current', disabled: 'Already Expedited' });
    expect(find(preApprovalStepRows(view), 'Express')).toMatchObject({ hint: 'current', disabled: 'Already Express' });
    expect(find(priorityStepRows(view), 'Paused').disabled).toBeUndefined();
    const flat = commandList({ ...person, canSet: true, view });
    expect(find(flat, 'Priority → Expedited').hint).toBe('current');
    expect(find(flat, 'Pre-approval → Express').hint).toBe('current');
  });

  it('mark the own level, not the inherited one', () => {
    const view = levelled({ priority: 'expedited', priorityOwn: 'normal', priorityFrom: 'AER-9' }).view!;
    expect(find(priorityStepRows(view), 'Normal').hint).toBe('current');
    expect(find(priorityStepRows(view), 'Expedited').hint).toBe('priority');
  });

  it('block Pre-approved with the permission sentence for a non-approver', () => {
    const list = commandList(levelled());
    expect(find(list, 'Pre-approval → Pre-approved')).toMatchObject({ hint: 'blocked', disabled: PERMISSION_TITLE });
    expect(find(list, 'Pre-approval → Express').disabled).toBeUndefined();
  });

  it('read an unknown project as not an approver', () => {
    expect(find(commandList(levelled({}, null)), 'Pre-approval → Pre-approved').disabled).toBe(PERMISSION_TITLE);
  });

  it('enable Pre-approved for an approver', () => {
    const row = find(commandList(levelled({}, project(true))), 'Pre-approval → Pre-approved');
    expect(row.disabled).toBeUndefined();
    expect(row.hint).toBe('pre-approval');
  });

  it('lock lowering out of Pre-approved for a non-approver, and not for an approver', () => {
    const locked = find(commandList(levelled({ preApprovalOwn: 'preapproved' })), 'Pre-approval → None');
    expect(locked).toMatchObject({ hint: 'blocked', disabled: PERMISSION_TITLE });
    const open = find(commandList(levelled({ preApprovalOwn: 'preapproved' }, project(true))), 'Pre-approval → None');
    expect(open.disabled).toBeUndefined();
  });

  it('let current win over blocked', () => {
    const row = find(commandList(levelled({ preApprovalOwn: 'preapproved' })), 'Pre-approval → Pre-approved');
    expect(row).toMatchObject({ hint: 'current', disabled: 'Already Pre-approved' });
  });

  it('run with the level they name, and open their steps', () => {
    const { fake, priorities, approvals, steps } = env();
    const list = commandList(levelled({}, project(true)));
    find(list, 'Priority → Expedited').run(fake);
    find(list, 'Priority → Paused').run(fake);
    find(list, 'Pre-approval → Express').run(fake);
    find(list, 'Pre-approval → Pre-approved').run(fake);
    find(list, 'Priority').run(fake);
    find(list, 'Pre-approval').run(fake);
    expect(priorities).toEqual(['expedited', 'paused']);
    expect(approvals).toEqual(['express', 'preapproved']);
    expect(steps).toEqual(['priority', 'preapproval']);
  });

  it('are reached by the level word first', () => {
    const list = commandList(levelled());
    expect(names(commandRows(list, 'expedited').rows)[0]).toBe('Priority → Expedited');
    expect(names(commandRows(list, 'paused').rows)[0]).toBe('Priority → Paused');
    expect(names(commandRows(list, 'express').rows)[0]).toBe('Pre-approval → Express');
    expect(names(commandRows(list, 'none').rows)[0]).toBe('Pre-approval → None');
  });

  it('put the opener first for its own name', () => {
    const list = commandList(levelled());
    expect(names(commandRows(list, 'priority').rows)[0]).toBe('Priority');
    expect(names(commandRows(list, 'preapproval').rows)[0]).toBe('Pre-approval');
  });

  it('put Priority before Status where a column shares a word', () => {
    const shared = [column(1, 'Normal'), column(2, 'Paused')];
    const list = commandList({ ...levelled(), view: viewOf({ statuses: shared, card: issue({ statusId: 1 }) }) });
    expect(names(commandRows(list, 'paused').rows).slice(0, 2)).toEqual(['Priority → Paused', 'Status → Paused']);
  });
});
