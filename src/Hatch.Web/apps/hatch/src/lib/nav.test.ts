import { describe, expect, it } from 'vitest';
import appSource from '../App.tsx?raw';
import { NAV, activeGroup, navFor, navRows } from './nav';
import type { NavEntry } from './nav';

// Excluded on purpose: /issues/:key is reached from a card, never the nav;
// /settings and * (the unknown-route fallback) are not pages a menu lists.
const EXCLUDED_ROUTES = new Set(['/issues/:key', '/settings', '*']);

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
    for (const to of tos) {
      expect(routes).toContain(to);
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

  it('is null for a top-level link and for a page with no group', () => {
    expect(activeGroup('/')).toBeNull();
    expect(activeGroup('/plan')).toBeNull();
    expect(activeGroup('/issues/HA-1')).toBeNull();
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

  it('puts Board and Plan first, with no group label', () => {
    const rows = navRows(false).slice(0, 2);
    expect(rows).toEqual([
      { to: '/', label: 'Board', end: true, groupLabel: null },
      { to: '/plan', label: 'Plan', groupLabel: null },
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

  it('flattens to exactly one row per link and per group row, with none dropped or duplicated', () => {
    expect(navRows(false)).toHaveLength(flatLength(navFor(false)));
    expect(navRows(true)).toHaveLength(flatLength(navFor(true)));
  });
});
