import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MeContext } from '../lib/useMe';
import type { Me } from '../types';
import { HomeRoute } from './HomeRoute';

/* The real pages need a router and fetches neither is given here, so each is
   mocked to a marker only HomeRoute's own choice could draw. */
vi.mock('./BoardPage', () => ({ BoardPage: () => <div>the board marker</div> }));
vi.mock('./AmbitionsPage', () => ({ AmbitionsPage: () => <div>the ambitions marker</div> }));

const meOf = (home?: Me['home']): Me => ({
  kind: 'person',
  name: 'Ada',
  role: 'user',
  configured: true,
  canSignOut: true,
  canSignIn: false,
  home,
});

const renderAs = (me: Me | null, settled: boolean): string =>
  renderToStaticMarkup(
    <MeContext.Provider value={{ me, isAdmin: false, settled }}>
      <HomeRoute />
    </MeContext.Provider>,
  );

describe('HomeRoute', () => {
  it('draws nothing before the first read has settled', () => {
    expect(renderAs(null, false)).toBe('');
    expect(renderAs(meOf('ambitions'), false)).toBe('');
  });

  it('draws Ambitions once settled for a person whose home is ambitions', () => {
    const html = renderAs(meOf('ambitions'), true);
    expect(html).toContain('the ambitions marker');
    expect(html).not.toContain('the board marker');
  });

  it('draws the board for board, for no home, and for nobody answered', () => {
    expect(renderAs(meOf('board'), true)).toContain('the board marker');
    expect(renderAs(meOf(undefined), true)).toContain('the board marker');
    expect(renderAs(null, true)).toContain('the board marker');
  });
});
