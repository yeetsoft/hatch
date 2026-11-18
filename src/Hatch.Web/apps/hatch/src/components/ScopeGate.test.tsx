import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { ReactElement } from 'react';
import { MeContext } from '../lib/useMe';
import type { MeScope, Me } from '../types';
import { BallotPage } from '../pages/BallotPage';
import { BoardPage } from '../pages/BoardPage';
import { DreamsPage } from '../pages/DreamsPage';
import { PlanPage } from '../pages/PlanPage';
import { ProjectsPage } from '../pages/ProjectsPage';
import { NO_SCOPE_MESSAGE, ScopeGate } from './ScopeGate';

/* No DOM runner, so only the gate's own answer is read: the five pages are
   rendered as exported, with no router around them, which is itself the
   proof that a `none` caller never reaches a page's hooks or its fetches. */

/* The pages are imported whole, and one of them reaches the client logger,
   which reads `window`, `screen` and more as it loads. Nothing here logs. */
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

const meOf = (scope?: MeScope): Me => ({
  kind: 'person',
  name: 'Ada',
  role: 'user',
  configured: true,
  canSignOut: true,
  canSignIn: false,
  scope,
});

const renderAs = (me: Me | null, page: ReactElement): string =>
  renderToStaticMarkup(
    <MeContext.Provider value={{ me, isAdmin: false }}>{page}</MeContext.Provider>,
  );

const none: MeScope = { mode: 'none', projectIds: [] };

describe('ScopeGate', () => {
  it('says the sentence once, and nothing else of the page, for a caller with no project in scope', () => {
    const pages: [string, ReactElement][] = [
      ['board', <BoardPage />],
      ['ballot', <BallotPage />],
      ['plan', <PlanPage />],
      ['projects', <ProjectsPage />],
      ['dreams', <DreamsPage />],
    ];

    for (const [name, page] of pages) {
      const html = renderAs(meOf(none), page);
      expect(html, name).toContain('No project is in your scope; an admin sets it on Manage → Users.');
      expect(html.split('No project is in your scope').length - 1, name).toBe(1);
    }
  });

  it('draws the sentence and not the children', () => {
    const html = renderAs(meOf(none), <ScopeGate><p>the page</p></ScopeGate>);

    expect(html).toContain(NO_SCOPE_MESSAGE);
    expect(html).not.toContain('the page');
  });

  it.each<[string, MeScope | undefined]>([
    ['all', { mode: 'all', projectIds: [] }],
    ['some', { mode: 'some', projectIds: [3] }],
    ['no scope at all', undefined],
  ])('draws the page for %s', (_name, scope) => {
    const html = renderAs(meOf(scope), <ScopeGate><p>the page</p></ScopeGate>);

    expect(html).toContain('the page');
    expect(html).not.toContain(NO_SCOPE_MESSAGE);
  });

  it('draws the page while the answer has not come, or never does', () => {
    expect(renderAs(null, <ScopeGate><p>the page</p></ScopeGate>)).toContain('the page');
  });
});
