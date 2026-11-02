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
  { kind: 'link', to: '/search', label: 'Search' },
  {
    kind: 'group',
    label: 'Track',
    // How the work is going, and what happened to it: the plan ahead, the
    // measures of flow, and the record behind both.
    rows: [
      { to: '/plan', label: 'Plan' },
      { to: '/reports', label: 'Reports' },
      { to: '/history', label: 'History' },
    ],
  },
  {
    kind: 'group',
    label: 'Agents',
    // Pages about the loop rather than about the board: what it is doing, what
    // it is told, what it cost, and how to join it.
    rows: [
      { to: '/runners', label: 'Runners' },
      { to: '/playbooks', label: 'Playbooks' },
      { to: '/leaderboard', label: 'Leaderboard' },
      { to: '/overnight', label: 'Overnight' },
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

/** The board's route, which the logo links to: the bar has no Board entry of
    its own, so the logo is the way home and `HOME_LABEL` is its accessible
    name. */
export const HOME = '/';
export const HOME_LABEL = 'Board';

/** Rows the phone menu lists that the desktop bar does not. Docs is one: on
    the desk it is a row of the settings menu, which the phone has no room to
    repeat. Listed last, under no heading. */
export const PHONE_ONLY: NavRow[] = [{ to: '/docs', label: 'Docs' }];

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

export interface NavRowFlat {
  to: string;
  label: string;
  end?: boolean;
  /** The group this row came from, or null for a top-level link - nothing to
      head it with. */
  groupLabel: string | null;
}

/** `navFor(isAdmin)`, flattened to one row per entry: a top-level link keeps
    its place with no heading, and a group's rows each carry that group's
    label, so the phone menu can draw a heading exactly where the label
    changes. */
export function navRows(isAdmin: boolean): NavRowFlat[] {
  const bar = navFor(isAdmin).flatMap((entry): NavRowFlat[] =>
    entry.kind === 'link'
      ? [{ to: entry.to, label: entry.label, end: entry.end, groupLabel: null }]
      : entry.rows.map((row) => ({ to: row.to, label: row.label, groupLabel: entry.label })),
  );
  return [...bar, ...PHONE_ONLY.map((row) => ({ to: row.to, label: row.label, end: row.end, groupLabel: null }))];
}

function matches(pathname: string, to: string): boolean {
  const path = pathname.toLowerCase();
  const target = to.toLowerCase();
  return path === target || path.startsWith(`${target}/`);
}

/** The group holding the page at `pathname`, by NavLink's own rule - equal, or
    equal up to a following "/", ignoring case - so a trigger and its marked
    row never disagree. Gated rows still count, so an Admin reaching a Manage
    page by URL still lights Manage. Top-level links are never a match, and
    neither is the board (`HOME`), which the logo lights instead. */
export function activeGroup(pathname: string): NavGroup | null {
  for (const entry of NAV) {
    if (entry.kind !== 'group') continue;
    if (entry.rows.some((row) => matches(pathname, row.to))) return entry;
  }
  return null;
}
