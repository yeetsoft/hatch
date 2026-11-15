import { describe, expect, it } from 'vitest';
import type { ThemeChoice } from '@hatch/ui';
import { COMMAND_PREVIEW, commandList, commandRows, commandStatus, offered, squash } from './consoleCommands';
import type { Command, CommandContext, CommandEnv } from './consoleCommands';
import { navRows } from './nav';

const admin: CommandContext = { isAdmin: true, pathname: '/' };
const person: CommandContext = { isAdmin: false, pathname: '/' };

const names = (commands: Command[]) => commands.map((c) => c.name);
const find = (commands: Command[], name: string) => commands.find((c) => c.name === name)!;

/* A recording fake for what a command runs against. */
function env() {
  const went: string[] = [];
  const themes: ThemeChoice[] = [];
  const filed = { count: 0 };
  const fake: CommandEnv = {
    go: (p) => went.push(p),
    setTheme: (c) => themes.push(c),
    newIssue: () => filed.count++,
  };
  return { fake, went, themes, filed };
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
  it('opens on Board, then the nav rows, then Settings, then New issue, then the themes', () => {
    const list = names(commandList(admin));
    expect(list[0]).toBe('Go to Board');
    expect(list[1]).toBe('Go to Search');
    expect(list.slice(-5)).toEqual(['Go to Settings', 'New issue', 'Theme → Auto', 'Theme → Light', 'Theme → Dark']);
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
      'Go to Dreams',
      'Go to Plan',
      'Go to Reports',
      'Go to History',
      'Go to Runners',
      'Go to Playbooks',
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
