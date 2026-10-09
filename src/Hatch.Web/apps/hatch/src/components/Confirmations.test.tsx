import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { Chicklet, ConfirmationStack } from './Confirmations';
import { raise, raiseMove, settle } from '../lib/confirmations';
import type { Confirmation } from '../lib/confirmations';

/* No DOM here, so this checks structure and not layout: that every kind of
   chicklet comes out of the one frame. What the width then does is lib/
   confirmationsCss.test.ts's, and the rest is the operator's browser. */
const noop = () => undefined;

// The key link is built against the address the document was served at, and
// matchMedia/navigator answer "not standalone" so existing tests keep their
// anchor-with-target shape.
beforeEach(() =>
  vi.stubGlobal('window', {
    location: { pathname: '/apps/hatch/' },
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

  it('routes the key in place, with no new tab and no ↗, when standalone', () => {
    vi.stubGlobal('window', {
      location: { pathname: '/apps/hatch/' },
      matchMedia: () => ({ matches: true, addEventListener: noop, removeEventListener: noop }),
      navigator: {},
    });

    const html = key(li(filing)[0]);

    expect(html).not.toContain('target="_blank"');
    expect(html).not.toContain('↗');
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
