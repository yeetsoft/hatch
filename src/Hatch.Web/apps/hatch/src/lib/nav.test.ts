import { describe, expect, it } from 'vitest';
import appSource from '../App.tsx?raw';
import { HOME, HOME_LABEL, NAV, PHONE_ONLY, activeGroup, navFor, navRows } from './nav';
import type { NavEntry } from './nav';

// Excluded on purpose: /issues/:key is reached from a card, and
// /reports/timeline, /reports/cumulative-flow and /reports/changelog from the
// Reports index, and /changelog from the docs Reference section, never the nav;
// /docs/* is the same page as /docs, for any path under it; / is the logo's and /docs the
// settings menu's and the phone list's (below), neither a bar entry; /settings and * (the
// unknown-route fallback) are not pages a menu lists. :name is /runners'
// nested detail route - the regex below captures a nested <Route>'s path
// literally, with no parent prefix - reached from a RunnerRow, never the nav.
const EXCLUDED_ROUTES = new Set([
  '/',
  '/docs',
  '/issues/:key',
  '/reports/timeline',
  '/reports/cumulative-flow',
  '/reports/changelog',
  '/changelog',
  '/docs/*',
  '/settings',
  ':name',
  '*',
]);

function routesInApp(): string[] {
  const matches = appSource.matchAll(/<Route path="([^"]+)"/g);
  return Array.from(matches, (match) => match[1]).filter((route) => !EXCLUDED_ROUTES.has(route));
}

function tosInNav(): string[] {
  const tos: string[] = [];
  for (const entry of NAV) {
    if (entry.kind === 'link') tos.push(entry.to);
    else tos.push(...entry.rows.map((row) => row.to));
  }
  return tos;
}

describe('NAV', () => {
  it('names every route in App.tsx exactly once, and names no route App.tsx does not have', () => {
    const routes = routesInApp();
    const tos = tosInNav();

    for (const route of routes) {
      expect(tos.filter((to) => to === route)).toHaveLength(1);
    }
    expect(appSource).toContain(`<Route path="${HOME}"`);
    expect(appSource).toContain('<Route path="/docs"');
    for (const to of tos) {
      expect(routes).toContain(to);
    }
  });

  it('redirects /changelog to the report, replacing the entry, with the old page gone', () => {
    expect(appSource).toContain('<Route path="/changelog" element={<Navigate to="/reports/changelog" replace />} />');
    expect(appSource).not.toContain('ChangelogPage');
  });
});

describe('the bar', () => {
  it('shows Search, Track, Agents and Manage and nothing else, for anybody', () => {
    for (const isAdmin of [false, true]) {
      expect(navFor(isAdmin).map((entry) => entry.label)).toEqual(['Search', 'Track', 'Agents', 'Manage']);
    }
  });

  it('has no Board and no Docs entry: the logo is home and the gear holds Docs', () => {
    const tos = tosInNav();
    expect(tos).not.toContain('/');
    expect(tos).not.toContain('/docs');
    expect(HOME).toBe('/');
    expect(HOME_LABEL).toBe('Board');
  });

  it('puts Dreams, Plan, Reports and History under Track, in that order', () => {
    const track = NAV.find((entry) => entry.kind === 'group' && entry.label === 'Track');
    if (!track || track.kind !== 'group') throw new Error('Track not found');
    expect(track.rows.map((row) => row.to)).toEqual(['/dreams', '/plan', '/reports', '/history']);
  });

  it('no longer lists History under Manage', () => {
    const manage = NAV.find((entry) => entry.kind === 'group' && entry.label === 'Manage');
    if (!manage || manage.kind !== 'group') throw new Error('Manage not found');
    expect(manage.rows.map((row) => row.to)).not.toContain('/history');
  });
});

describe('Docs on the phone', () => {
  it('is the last row of the phone list, with no group label, for anybody', () => {
    expect(PHONE_ONLY).toEqual([{ to: '/docs', label: 'Docs' }]);
    for (const isAdmin of [false, true]) {
      const rows = navRows(isAdmin);
      expect(rows[rows.length - 1]).toEqual({ to: '/docs', label: 'Docs', end: undefined, groupLabel: null });
    }
  });
});

describe('activeGroup', () => {
  it('finds Agents from any of its rows', () => {
    expect(activeGroup('/runners')?.label).toBe('Agents');
    expect(activeGroup('/runner')?.label).toBe('Agents');
    expect(activeGroup('/playbooks')?.label).toBe('Agents');
  });

  it('finds Manage from an admin-only row', () => {
    expect(activeGroup('/users')?.label).toBe('Manage');
  });

  it('finds Track from Dreams, Plan, Reports, History and a report under it', () => {
    for (const path of ['/dreams', '/plan', '/reports', '/reports/timeline', '/history']) {
      expect(activeGroup(path)?.label).toBe('Track');
    }
  });

  it('is null for the board, a top-level link and a page with no group', () => {
    expect(activeGroup('/')).toBeNull();
    expect(activeGroup('/search')).toBeNull();
    expect(activeGroup('/issues/HA-1')).toBeNull();
    expect(activeGroup('/docs')).toBeNull();
    expect(activeGroup('/docs/x')).toBeNull();
  });

  it('is null on a string prefix that is not a path segment', () => {
    expect(activeGroup('/importer')).toBeNull();
  });
});

describe('navFor', () => {
  function manage(entries: NavEntry[]) {
    const group = entries.find((entry) => entry.kind === 'group' && entry.label === 'Manage');
    if (!group || group.kind !== 'group') throw new Error('Manage not found');
    return group;
  }

  it('drops the admin-only rows for anybody else', () => {
    const rows = manage(navFor(false)).rows.map((row) => row.to);
    expect(rows).not.toContain('/users');
    expect(rows).not.toContain('/api-keys');
  });

  it('ends Manage with Users then API keys for an Admin', () => {
    const rows = manage(navFor(true)).rows.map((row) => row.to);
    expect(rows.slice(-2)).toEqual(['/users', '/api-keys']);
  });
});

describe('navRows', () => {
  function flatLength(entries: NavEntry[]): number {
    return entries.reduce((sum, entry) => sum + (entry.kind === 'link' ? 1 : entry.rows.length), 0);
  }

  it('puts Search first with no group label, then Track under its heading', () => {
    const rows = navRows(false).slice(0, 5);
    expect(rows).toEqual([
      { to: '/search', label: 'Search', end: undefined, groupLabel: null },
      { to: '/dreams', label: 'Dreams', groupLabel: 'Track' },
      { to: '/plan', label: 'Plan', groupLabel: 'Track' },
      { to: '/reports', label: 'Reports', groupLabel: 'Track' },
      { to: '/history', label: 'History', groupLabel: 'Track' },
    ]);
  });

  it('carries every Agents row with the Agents group label, in order', () => {
    const agents = navFor(false).find((entry) => entry.kind === 'group' && entry.label === 'Agents');
    if (!agents || agents.kind !== 'group') throw new Error('Agents not found');

    const rows = navRows(false).filter((row) => row.groupLabel === 'Agents');
    expect(rows.map((row) => row.to)).toEqual(agents.rows.map((row) => row.to));
  });

  it('drops the admin-only Manage rows for anybody else, and keeps them for an Admin', () => {
    const tos = (rows: ReturnType<typeof navRows>) => rows.map((row) => row.to);

    expect(tos(navRows(false))).not.toContain('/users');
    expect(tos(navRows(false))).not.toContain('/api-keys');
    expect(tos(navRows(true))).toContain('/users');
    expect(tos(navRows(true))).toContain('/api-keys');
  });

  it('flattens to exactly one row per link and per group row plus the phone-only ones, with none dropped or duplicated', () => {
    expect(navRows(false)).toHaveLength(flatLength(navFor(false)) + PHONE_ONLY.length);
    expect(navRows(true)).toHaveLength(flatLength(navFor(true)) + PHONE_ONLY.length);
  });
});
