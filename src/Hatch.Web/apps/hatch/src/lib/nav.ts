export interface NavRow {
  to: string;
  label: string;
  /** Exact-match only, the way NavLink's own `end` works: without it a link to
      "/" would also light on every other route. */
  end?: boolean;
  adminOnly?: boolean;
}

export interface NavGroup {
  label: string;
  rows: NavRow[];
}

export type NavEntry = ({ kind: 'link' } & NavRow) | ({ kind: 'group' } & NavGroup);

/**
 * Every page the primary nav shows, and the one place it lists them.
 *
 * This is the file that changes when a page moves from one menu to the other,
 * or gets a new name: one line, not a hunt through App.tsx and PrimaryNav.tsx
 * for every place the old label was written.
 */
export const NAV: NavEntry[] = [
  { kind: 'link', to: '/', label: 'Board', end: true },
  { kind: 'link', to: '/plan', label: 'Plan' },
  {
    kind: 'group',
    label: 'Agents',
    // Pages about the loop rather than about the board: what it is doing, what
    // it is told, what it cost, and how to join it.
    rows: [
      { to: '/runners', label: 'Runners' },
      { to: '/playbooks', label: 'Playbooks' },
      { to: '/leaderboard', label: 'Leaderboard' },
      { to: '/runner', label: 'Get the runner' },
    ],
  },
  {
    kind: 'group',
    label: 'Manage',
    // Pages about the board's shape and data: what it looks like, what comes
    // into it, what changes in bulk, and last the operator's own controls.
    rows: [
      { to: '/projects', label: 'Projects' },
      { to: '/statuses', label: 'Statuses' },
      { to: '/import', label: 'Import' },
      { to: '/bulk', label: 'Bulk edit' },
      // Admin only, and only where the wall gives anybody a role.
      { to: '/users', label: 'Users', adminOnly: true },
      { to: '/api-keys', label: 'API keys', adminOnly: true },
    ],
  },
];

/** `NAV`, with the admin-only rows dropped for anybody else, and a group left
    with nothing in it dropped along with them - a menu never opens on
    nothing. */
export function navFor(isAdmin: boolean): NavEntry[] {
  return NAV.map((entry): NavEntry | null => {
    if (entry.kind === 'link') return entry;
    const rows = isAdmin ? entry.rows : entry.rows.filter((row) => !row.adminOnly);
    return rows.length > 0 ? { ...entry, rows } : null;
  }).filter((entry): entry is NavEntry => entry !== null);
}

function matches(pathname: string, to: string): boolean {
  const path = pathname.toLowerCase();
  const target = to.toLowerCase();
  return path === target || path.startsWith(`${target}/`);
}

/** The group holding the page at `pathname`, by NavLink's own rule - equal, or
    equal up to a following "/", ignoring case - so a trigger and its marked
    row never disagree. Gated rows still count, so an Admin reaching a Manage
    page by URL still lights Manage. Top-level links are never a match. */
export function activeGroup(pathname: string): NavGroup | null {
  for (const entry of NAV) {
    if (entry.kind !== 'group') continue;
    if (entry.rows.some((row) => matches(pathname, row.to))) return entry;
  }
  return null;
}
