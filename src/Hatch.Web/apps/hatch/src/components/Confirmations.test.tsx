import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { Chicklet, ConfirmationStack } from './Confirmations';
import { raise, raiseMove, settle } from '../lib/confirmations';
import type { Confirmation } from '../lib/confirmations';
import { ScopeProvider } from '../lib/useScope';
import type { Project } from '../types';

vi.mock('../lib/useProjects', async (original) => ({
  ...(await original<typeof import('../lib/useProjects')>()),
  useProjects: () => ({ projects: [{ key: 'AER' }, { key: 'FIL' }, { key: 'HA' }] as Project[], byKey: new Map(), loaded: true }),
}));

/* No DOM here, so this checks structure and not layout: that every kind of
   chicklet comes out of the one frame. What the width then does is lib/
   confirmationsCss.test.ts's, and the rest is docs/browser-testing.md's, not
   the operator's alone. */
const noop = () => undefined;

// The key link is built against the address the document was served at, and
// matchMedia/navigator answer "not standalone" so existing tests keep their
// anchor-with-target shape.
beforeEach(() =>
  vi.stubGlobal('window', {
    location: { pathname: '/' },
    matchMedia: () => ({ matches: false, addEventListener: noop, removeEventListener: noop }),
    navigator: {},
  }),
);
afterEach(() => vi.unstubAllGlobals());

const filing = raise([], { key: 'AER-1', title: 'A filing' }, 1, 15_000);
const move = raiseMove(
  [],
  {
    issueKey: 'AER-2',
    title: 'A move with a title long enough to want two lines in a corner this narrow',
    from: { id: 1, name: 'Backlog' },
    to: { id: 2, name: 'To Do' },
    restore: { statusId: 1, afterKey: null, beforeKey: null, fromStatusId: 2 },
  },
  2,
  15_000,
);
const settled = settle(move, 2, 'undone', 'moved back to Backlog');

const li = (stack: Confirmation[]) => {
  const html = renderToStaticMarkup(
    <MemoryRouter>
      <ConfirmationStack stack={stack} onUndo={noop} onDismiss={noop} onDismissAll={noop} />
    </MemoryRouter>,
  );
  return [...html.matchAll(/<li\b[^>]*>[\s\S]*?<\/li>/g)].map((m) => m[0]);
};

const liAt = (entry: string, stack: Confirmation[]) =>
  renderToStaticMarkup(
    <MemoryRouter initialEntries={[entry]}>
      <ScopeProvider>
        <ConfirmationStack stack={stack} onUndo={noop} onDismiss={noop} onDismissAll={noop} />
      </ScopeProvider>
    </MemoryRouter>,
  );

describe('Chicklet', () => {
  it('is the same outer element and class for a filing, a move, and a move after its undo', () => {
    const opening = (stack: Confirmation[]) => /^<li[^>]*>/.exec(li(stack)[0])![0];

    expect(opening(filing)).toBe('<li class="hatch-confirmation">');
    expect(opening(move)).toBe(opening(filing));
    expect(opening(settled)).toBe(opening(filing));
  });

  it('ends in the × for every kind, so it is at the same edge on each', () => {
    for (const stack of [filing, move, settled]) {
      expect(li(stack)[0]).toMatch(/<button[^>]*class="hatch-confirmation-close"[^>]*>×<\/button><\/li>$/);
    }
  });

  it('hints what the × closes, in the words a screen reader hears', () => {
    const close = li(filing)[0].match(/<button[^>]*class="hatch-confirmation-close"[^>]*>/)![0];
    const label = /aria-label="([^"]+)"/.exec(close)![1];
    expect(close).toContain(`title="${label}"`);
  });

  it('puts the key, then the body with the title, then the ×', () => {
    const html = li(filing)[0];

    expect(html.indexOf('hatch-confirmation-key')).toBeLessThan(html.indexOf('hatch-confirmation-body'));
    expect(html.indexOf('hatch-confirmation-body')).toBeLessThan(html.indexOf('hatch-confirmation-close'));
    expect(html).toContain('A filing');
  });

  it('draws what it is given as children inside the body, after the title', () => {
    const html = renderToStaticMarkup(
      <ul>
        <Chicklet c={filing[0]} onDismiss={noop}>
          <span>a new kind of body</span>
        </Chicklet>
      </ul>,
    );

    expect(html).toMatch(/hatch-confirmation-title[\s\S]*a new kind of body<\/span><\/span><button/);
  });

  const key = (html: string) => /<a[^>]*class="hatch-confirmation-key"[^>]*>[\s\S]*?<\/a>/.exec(html)![0];

  it('opens the key in a new tab, marked with ↗, when not standalone', () => {
    const html = key(li(filing)[0]);

    expect(html).toContain('target="_blank"');
    expect(html).toContain('↗');
  });

  it('carries the scope on the key link when one is active', () => {
    expect(key(liAt('/?scope=AER,FIL', filing))).toContain('href="/issues/AER-1?scope=AER,FIL"');
  });

  it('leaves the key link bare when the scope is empty, or there is no provider', () => {
    expect(key(liAt('/', filing))).toContain('href="/issues/AER-1"');
    expect(key(liAt('/', filing))).not.toContain('scope');
    expect(key(li(filing)[0])).not.toContain('scope');
  });

  it('leaves the key link bare when the scope names every project', () => {
    expect(key(liAt('/?scope=AER,FIL,HA', filing))).not.toContain('scope');
  });

  it('routes the key in place, with no new tab and no ↗, when standalone', () => {
    vi.stubGlobal('window', {
      location: { pathname: '/' },
      matchMedia: () => ({ matches: true, addEventListener: noop, removeEventListener: noop }),
      navigator: {},
    });

    const html = key(li(filing)[0]);

    expect(html).not.toContain('target="_blank"');
    expect(html).not.toContain('↗');
  });

  it('leaves the scope to the keeper on the routed key, standalone', () => {
    vi.stubGlobal('window', {
      location: { pathname: '/' },
      matchMedia: () => ({ matches: true, addEventListener: noop, removeEventListener: noop }),
      navigator: {},
    });

    expect(key(liAt('/?scope=AER,FIL', filing))).not.toContain('scope');
  });
});

describe('ConfirmationStack', () => {
  it('draws nothing but chicklets in its list', () => {
    const stack = [...move, ...filing];

    expect(li(stack)).toHaveLength(2);
    const html = renderToStaticMarkup(
      <ConfirmationStack stack={stack} onUndo={noop} onDismiss={noop} onDismissAll={noop} />,
    );
    expect(html).toMatch(/<ul class="hatch-confirmations-list">(<li class="hatch-confirmation">[\s\S]*?<\/li>){2}<\/ul>/);
  });
});
