import { describe, expect, it } from 'vitest';
import { NAV } from './nav';
import { DOC_PAGES, docPage } from './docs';
import { APP_BASENAME } from './basename';

const SCREENS: Record<string, string> = {
  '/': 'the-board-and-its-columns',
  '/plan': 'issues',
  '/reports': 'flow-wip-and-the-battery',
  '/search': 'getting-started',
  '/runners': 'watching-the-loop',
  '/leaderboard': 'watching-the-loop',
  '/overnight': 'watching-the-loop',
  '/history': 'watching-the-loop',
  '/playbooks': 'playbooks',
  '/runner': 'the-runner',
  '/projects': 'getting-started',
  '/statuses': 'the-board-and-its-columns',
  '/import': 'issues',
  '/bulk': 'issues',
  '/users': 'people-roles-and-keys',
  '/api-keys': 'people-roles-and-keys',
  '/docs': 'welcome',
};

// NAV itself, flattened to {to,label}: navFor(isAdmin) drops the admin-only
// rows (/users, /api-keys) this test must still cover.
const rows: { to: string; label: string }[] = NAV.flatMap((e) =>
  e.kind === 'link' ? [{ to: e.to, label: e.label }] : e.rows.map((r) => ({ to: r.to, label: r.label })),
);

const escapeRegExp = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

function namesScreen(body: string, label: string, to: string): boolean {
  const asLabel = new RegExp(`(?<![\\w-])${escapeRegExp(label)}(?![\\w-])`).test(body);
  return asLabel || body.includes(`\`${to}\``) || body.includes(`${APP_BASENAME}${to}`);
}

describe('every NAV row is in SCREENS', () => {
  it.each(rows.map((r) => r.to))('%s is a key of SCREENS', (to) => {
    expect(Object.keys(SCREENS), `${to} is in NAV but not in SCREENS`).toContain(to);
  });
});

describe('every SCREENS key is a NAV row', () => {
  it.each(Object.keys(SCREENS))('%s is a NAV row', (to) => {
    expect(
      rows.map((r) => r.to),
      `${to} is in SCREENS but not in NAV`,
    ).toContain(to);
  });
});

describe('every SCREENS value names a real docs page', () => {
  it.each(Object.values(SCREENS))('%s is a docs page', (slug) => {
    expect(docPage(DOC_PAGES, slug), `SCREENS points at "${slug}", which is not a docs page`).toBeDefined();
  });
});

describe('every primary-nav screen is named on the docs page that explains it', () => {
  it.each(rows.map((r): [string, string, string] => [r.to, r.label, SCREENS[r.to]]))(
    '%s (%s) is named on %s',
    (to, label, slug) => {
      const page = docPage(DOC_PAGES, slug);
      expect(page).toBeDefined();
      expect(
        namesScreen(page!.body, label, to),
        `${slug}.md does not name the ${label} screen (${to}): add "${label}" to it, or point SCREENS at the page that explains it`,
      ).toBe(true);
    },
  );
});
