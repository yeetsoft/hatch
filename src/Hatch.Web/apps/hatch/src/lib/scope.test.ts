import { createMemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import {
  SCOPE_CLEARED,
  cardsInScope,
  keepScope,
  knownKeys,
  parseScope,
  projectsInScope,
  rawScope,
  scopeProjectIds,
  scopeSearch,
  scopeTitle,
  scopeWithProject,
  scopedPath,
  withScope,
} from './scope';
import type { Project } from '../types';

const project = (over: Partial<Project>): Project => ({
  id: 1,
  key: 'AER',
  name: 'Aerial',
  issueCount: 0,
  createdAt: '2026-01-01T00:00:00Z',
  color: null,
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  members: [],
  canApprove: false,
  canAdminister: false,
  landing: 'pull-request',
  checksRequired: true,
  onePullRequest: false,
  autoHealBugEnabled: false,
  autoHealBugPriority: 'emergency',
  autoHealBugPreApproval: 'express',
  autoHealBugStatusId: null,
  repoSourced: false,
  repoSourcedBy: null,
  repoSourcedAt: null,
  bindsTracker: false,
  bindsTrackerBy: null,
  bindsTrackerAt: null,
  reflectionCadenceDays: null,
  reflectionLastPeriodStart: null,
  reflectionNextDueAt: null,
  storiesOnEpics: false,
  checkApprovesMerge: false,
  checkApprovesMergeBy: null,
  checkApprovesMergeAt: null,
  noCodeCloses: false,
  noCodeClosesBy: null,
  noCodeClosesAt: null,
  onePullRequestPolicy: 'optional',
  onePullRequestPolicyBy: null,
  onePullRequestPolicyAt: null,
  ...over,
});

const projects = [
  project({ id: 1, key: 'AER' }),
  project({ id: 2, key: 'FIL' }),
  project({ id: 3, key: 'ZED' }),
];
const allKeys = projects.map((p) => p.key);
const q = (search: string) => new URLSearchParams(search);

describe('rawScope', () => {
  it('splits, trims, and drops empties and duplicates, consulting no list', () => {
    expect(rawScope(q('scope=AER, FIL,,AER,NOPE'))).toEqual(['AER', 'FIL', 'NOPE']);
  });

  it('is empty with no param or an empty one', () => {
    expect(rawScope(q(''))).toEqual([]);
    expect(rawScope(q('scope='))).toEqual([]);
  });

  it('accepts the encoded comma', () => {
    expect(rawScope(q('scope=AER%2CFIL'))).toEqual(['AER', 'FIL']);
  });
});

describe('knownKeys', () => {
  it('drops an unknown key silently', () => {
    expect(knownKeys(['AER', 'NOPE'], projects)).toEqual(['AER']);
  });

  it("answers in the project list's order", () => {
    expect(knownKeys(['FIL', 'AER'], projects)).toEqual(['AER', 'FIL']);
  });

  it('answers none when every key is named', () => {
    expect(knownKeys(['AER', 'FIL', 'ZED'], projects)).toEqual([]);
  });

  it('answers none when nothing is named', () => {
    expect(knownKeys([], projects)).toEqual([]);
  });

  it('always answers none for a single-project list', () => {
    expect(knownKeys(['AER'], [project({ key: 'AER' })])).toEqual([]);
  });

  it('answers none for no projects', () => {
    expect(knownKeys(['AER'], [])).toEqual([]);
  });
});

describe('scopeProjectIds', () => {
  it('answers none when no keys are named', () => {
    expect(scopeProjectIds([], projects)).toEqual([]);
  });

  it("answers in the project list's order", () => {
    expect(scopeProjectIds(['ZED', 'AER'], projects)).toEqual([1, 3]);
  });

  it('drops an unknown key silently', () => {
    expect(scopeProjectIds(['AER', 'NOPE'], projects)).toEqual([1]);
  });
});

describe('parseScope', () => {
  it('drops an unknown key silently', () => {
    expect(parseScope(q('scope=AER,NOPE'), projects)).toEqual(['AER']);
  });

  it('drops a duplicate key', () => {
    expect(parseScope(q('scope=AER,AER,FIL'), projects)).toEqual(['AER', 'FIL']);
  });

  it('answers none when every key is named', () => {
    expect(parseScope(q('scope=AER,FIL,ZED'), projects)).toEqual([]);
  });

  it('answers none with no param or an empty one', () => {
    expect(parseScope(q(''), projects)).toEqual([]);
    expect(parseScope(q('scope='), projects)).toEqual([]);
  });

  it('answers none when only unknown keys are named', () => {
    expect(parseScope(q('scope=NOPE'), projects)).toEqual([]);
  });

  it("answers in the project list's order", () => {
    expect(parseScope(q('scope=FIL,AER'), projects)).toEqual(['AER', 'FIL']);
  });

  it('always answers none for a single-project list', () => {
    expect(parseScope(q('scope=AER'), [project({ key: 'AER' })])).toEqual([]);
  });

  it('answers none for no projects', () => {
    expect(parseScope(q('scope=AER'), [])).toEqual([]);
  });
});

describe('cardsInScope', () => {
  const cards = [{ projectKey: 'AER' }, { projectKey: 'FIL' }, { projectKey: 'ZED' }];

  it('answers the same array for an empty scope', () => {
    expect(cardsInScope(cards, [])).toBe(cards);
  });

  it('drops the card outside the scope, order kept', () => {
    expect(cardsInScope(cards, ['ZED', 'AER'])).toEqual([{ projectKey: 'AER' }, { projectKey: 'ZED' }]);
  });
});

describe('projectsInScope', () => {
  const list = [{ key: 'AER' }, { key: 'FIL' }, { key: 'ZED' }];

  it('answers the same array for an empty scope', () => {
    expect(projectsInScope(list, [])).toBe(list);
  });

  it('keeps the projects in the scope, in list order', () => {
    expect(projectsInScope(list, ['ZED', 'AER'])).toEqual([{ key: 'AER' }, { key: 'ZED' }]);
  });

  it('ignores a key the list lacks', () => {
    expect(projectsInScope(list, ['FIL', 'NOPE'])).toEqual([{ key: 'FIL' }]);
  });
});

describe('withScope', () => {
  it('writes the canonical comma form in allKeys order', () => {
    expect(withScope(q(''), ['FIL', 'AER'], allKeys).get('scope')).toBe('AER,FIL');
  });

  it('removes the param for no keys', () => {
    expect(withScope(q('scope=AER'), [], allKeys).has('scope')).toBe(false);
  });

  it('removes the param for every key', () => {
    expect(withScope(q('scope=AER'), ['ZED', 'AER', 'FIL'], allKeys).has('scope')).toBe(false);
  });

  it('keeps other params and their order', () => {
    const next = withScope(q('a=1&scope=AER&b=2'), ['FIL'], allKeys);
    expect(next.toString()).toBe('a=1&scope=FIL&b=2');
  });

  it('never mutates the input', () => {
    const params = q('scope=AER&a=1');
    withScope(params, ['FIL'], allKeys);
    withScope(params, [], allKeys);
    expect(params.toString()).toBe('scope=AER&a=1');
  });
});

describe('scopeSearch', () => {
  it('writes the comma literally', () => {
    expect(scopeSearch(withScope(q(''), ['AER', 'FIL'], allKeys))).toBe('?scope=AER,FIL');
  });

  it('is empty for no params', () => {
    expect(scopeSearch(q(''))).toBe('');
  });
});

describe('scopedPath', () => {
  it('appends the scope when there is one', () => {
    expect(scopedPath('/plan', ['AER', 'FIL'])).toBe('/plan?scope=AER,FIL');
  });

  it('leaves the path alone when there is none', () => {
    expect(scopedPath('/plan', [])).toBe('/plan');
  });
});

describe('scopeTitle', () => {
  it('joins the scoped keys with the Hatch suffix', () => {
    expect(scopeTitle(['AER', 'FIL'])).toBe('AER, FIL - Hatch');
  });

  it('is just Hatch for no scope', () => {
    expect(scopeTitle([])).toBe('Hatch');
  });
});

describe('keepScope', () => {
  const at = (search: string, over: Partial<Parameters<typeof keepScope>[0]['location']> = {}) => ({
    pathname: '/plan',
    search,
    hash: '',
    state: null,
    ...over,
  });

  it('1: a scope in the URL is the scope, with no redirect', () => {
    expect(
      keepScope({ location: at('?scope=FIL,NOPE'), navigationType: 'PUSH', carried: ['AER'] }),
    ).toEqual({ carried: ['FIL', 'NOPE'], redirect: null });
  });

  it('1: an empty scope in the URL is an empty scope', () => {
    expect(keepScope({ location: at('?scope='), navigationType: 'PUSH', carried: ['AER'] })).toEqual({
      carried: [],
      redirect: null,
    });
  });

  it('2: a cleared flag in state carries nothing', () => {
    expect(
      keepScope({
        location: at('', { state: { [SCOPE_CLEARED]: true } }),
        navigationType: 'REPLACE',
        carried: ['AER'],
      }),
    ).toEqual({ carried: [], redirect: null });
  });

  it('2: only a true flag counts', () => {
    const step = keepScope({
      location: at('', { state: { [SCOPE_CLEARED]: 'yes' } }),
      navigationType: 'PUSH',
      carried: ['AER'],
    });
    expect(step.redirect).not.toBeNull();
  });

  it('3: a POP carries nothing and never redirects', () => {
    expect(keepScope({ location: at(''), navigationType: 'POP', carried: ['AER'] })).toEqual({
      carried: [],
      redirect: null,
    });
  });

  it('4: nothing carried is nothing to do', () => {
    expect(keepScope({ location: at('?x=1'), navigationType: 'PUSH', carried: [] })).toEqual({
      carried: [],
      redirect: null,
    });
  });

  it('5: otherwise the carry is put back on the same location', () => {
    expect(
      keepScope({ location: at(''), navigationType: 'PUSH', carried: ['AER', 'FIL'] }),
    ).toEqual({
      carried: ['AER', 'FIL'],
      redirect: { pathname: '/plan', search: '?scope=AER,FIL', hash: '' },
    });
  });

  it("5: a page's own params survive, in order, and so does the hash", () => {
    const step = keepScope({
      location: at('?project=FIL&projectId=2', { hash: '#c9' }),
      navigationType: 'REPLACE',
      carried: ['AER'],
    });
    expect(step.redirect).toEqual({
      pathname: '/plan',
      search: '?project=FIL&projectId=2&scope=AER',
      hash: '#c9',
    });
  });
});

/* The rule driven through a real router, as the provider's effect will drive
   it: keepScope on the initial location and on every location change, and a
   REPLACE when it asks for one. */
function keeper(initialSearch: string) {
  const router = createMemoryRouter([{ path: '*', element: null }], {
    initialEntries: [{ pathname: '/', search: initialSearch }],
  });
  const actions: string[] = [];
  let carried: string[] = [];
  let handled: string | null = null;

  const step = () => {
    const { location, historyAction } = router.state;
    if (location.key === handled) return;
    handled = location.key;
    actions.push(historyAction);

    const result = keepScope({ location, navigationType: historyAction, carried });
    carried = result.carried;
    if (result.redirect) void router.navigate(result.redirect, { replace: true, state: location.state });
  };

  router.initialize();
  step();
  router.subscribe(step);

  return {
    router,
    actions,
    carried: () => carried,
    where: () => router.state.location.pathname + router.state.location.search,
    back: async () => {
      await router.navigate(-1);
      await Promise.resolve();
    },
  };
}

describe('keepScope in a router', () => {
  it('puts the scope on a plain click, one history entry per click', async () => {
    const k = keeper('?scope=AER,FIL');
    expect(k.carried()).toEqual(['AER', 'FIL']);

    await k.router.navigate('/plan');
    expect(k.where()).toBe('/plan?scope=AER,FIL');
    expect(k.actions).toEqual(['POP', 'PUSH', 'REPLACE']);

    await k.back();
    expect(k.where()).toBe('/?scope=AER,FIL');
    expect(k.router.state.historyAction).toBe('POP');
  });

  it('does the same for an issue page', async () => {
    const k = keeper('?scope=AER,FIL');

    await k.router.navigate('/issues/AER-12');
    expect(k.where()).toBe('/issues/AER-12?scope=AER,FIL');
    expect(k.actions.slice(1)).toEqual(['PUSH', 'REPLACE']);

    await k.back();
    expect(k.where()).toBe('/?scope=AER,FIL');
  });

  it('keeps the scope through a redirect', async () => {
    const k = keeper('?scope=AER,FIL');

    await k.router.navigate('/changelog');
    await k.router.navigate('/reports/changelog', { replace: true });
    expect(k.where()).toBe('/reports/changelog?scope=AER,FIL');
  });

  it('does not re-apply a cleared scope', async () => {
    const k = keeper('?scope=AER,FIL');

    await k.router.navigate(
      { pathname: '/', search: '' },
      { replace: true, state: { [SCOPE_CLEARED]: true } },
    );
    expect(k.where()).toBe('/');
    expect(k.carried()).toEqual([]);

    await k.router.navigate('/plan');
    expect(k.where()).toBe('/plan');
  });

  it('never redirects a fresh load with no param', async () => {
    const k = keeper('');
    expect(k.where()).toBe('/');
    expect(k.actions).toEqual(['POP']);

    await k.router.navigate('/plan');
    expect(k.where()).toBe('/plan');
    expect(k.actions).toEqual(['POP', 'PUSH']);
  });
});

describe('scopeWithProject', () => {
  it('appends the key', () => {
    expect(scopeWithProject(['AER'], 'HA')).toEqual(['AER', 'HA']);
  });
});
