import { afterEach, describe, expect, it, vi } from 'vitest';
import { UNASSIGNED, assigneeToken } from './assignee';
import {
  BOARD_TYPES_STORAGE_KEY,
  DEFAULT_FILTER,
  DEFAULT_TYPES,
  assigneeFacets,
  filterCards,
  isFiltering,
  matchesQuery,
  readBoardTypes,
  revealType,
  toggleType,
  toggleWaiting,
  typeCounts,
  typesSummary,
  writeBoardTypes,
} from './filter';
import { ISSUE_TYPES } from '../types';
import type { Assignee, IssueCard } from '../types';

/** A localStorage that holds what it is given, and can be made to throw - the
    same stub phoneBoard.test.ts uses for its storage pair. */
function stubStorage(initial: Record<string, string> = {}, throws = false) {
  const data = { ...initial };
  const storage = {
    getItem: (k: string) => {
      if (throws) throw new Error('blocked');
      return k in data ? data[k] : null;
    },
    setItem: (k: string, v: string) => {
      if (throws) throw new Error('blocked');
      data[k] = v;
    },
  };
  vi.stubGlobal('window', { localStorage: storage });
  return data;
}

afterEach(() => vi.unstubAllGlobals());

const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'AER-1',
  projectKey: 'AER',
  type: 'task',
  title: 'Renew the wildcard certificate',
  statusId: 1,
  rank: 1024,
  parentKey: null,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  ...over,
});

const person = (name: string, id = `p-${name}`): Assignee => ({ kind: 'person', id, name });
const key = (name: string, id = `k-${name}`): Assignee => ({ kind: 'key', id, name });

/* Every literal filter below is spread onto this rather than written whole, so
   that adding a switch to CardFilter is one edit here and not one per case. */
const filter = (over: Partial<typeof DEFAULT_FILTER> = {}) => ({ ...DEFAULT_FILTER, ...over });

describe('matchesQuery', () => {
  it('matches nothing in particular when nothing was typed', () => {
    expect(matchesQuery(card(), '')).toBe(true);
    expect(matchesQuery(card(), '   ')).toBe(true);
  });

  it('reads a title in any case', () => {
    expect(matchesQuery(card(), 'WILDCARD')).toBe(true);
    expect(matchesQuery(card({ title: 'FEED THE CAT' }), 'cat')).toBe(true);
  });

  it('finds a card by the key somebody pasted in', () => {
    expect(matchesQuery(card({ key: 'OPS-42' }), 'ops-42')).toBe(true);
  });

  it('finds a card by its type and by the parent it hangs under', () => {
    expect(matchesQuery(card({ type: 'bug' }), 'bug')).toBe(true);
    expect(matchesQuery(card({ parentKey: 'AER-9' }), 'aer-9')).toBe(true);
  });

  /* The whole point of splitting on whitespace: nobody remembers the word order
     of a title they wrote three weeks ago. */
  it('takes several terms in any order, and wants all of them', () => {
    expect(matchesQuery(card(), 'certificate renew')).toBe(true);
    expect(matchesQuery(card(), 'renew kitchen')).toBe(false);
  });

  it('says no when a term appears nowhere', () => {
    expect(matchesQuery(card(), 'thermostat')).toBe(false);
  });
});

describe('filterCards', () => {
  const cards = [
    card({ key: 'AER-1', type: 'epic', title: 'The plan' }),
    card({ key: 'AER-2', type: 'bug', title: 'The certificate expired' }),
    card({ key: 'AER-3', type: 'task', title: 'Renew the certificate' }),
  ];

  it('draws the whole board when nothing is filtered', () => {
    expect(filterCards(cards, DEFAULT_FILTER)).toHaveLength(3);
  });

  it('keeps only the chosen types', () => {
    expect(filterCards(cards, filter({ types: ['bug'] })).map((c) => c.key)).toEqual(['AER-2']);
  });

  it('takes more than one type at a time', () => {
    expect(filterCards(cards, filter({ types: ['bug', 'epic'] })).map((c) => c.key)).toEqual(['AER-1', 'AER-2']);
  });

  it('opens on every type, tasks included', () => {
    expect(filterCards(cards, DEFAULT_FILTER).map((c) => c.key)).toEqual(['AER-1', 'AER-2', 'AER-3']);
  });

  it('hides tasks once they are switched off', () => {
    expect(filterCards(cards, toggleType(DEFAULT_FILTER, 'task')).map((c) => c.key)).toEqual(['AER-1', 'AER-2']);
  });

  it('applies the type filter and the search together', () => {
    expect(filterCards(cards, filter({ types: ['task'], query: 'certificate' })).map((c) => c.key)).toEqual(['AER-3']);
  });

  it('keeps only the cards holding an unanswered question', () => {
    const asked = [...cards, card({ key: 'AER-4', title: 'Retry policy', openQuestions: 2 })];

    expect(filterCards(asked, filter({ waiting: true })).map((c) => c.key)).toEqual(['AER-4']);
  });

  /* The switch narrows alongside everything else rather than replacing it, so
     "the bugs that are waiting on me" is one board and not two passes. */
  it('narrows with the type filter rather than instead of it', () => {
    const asked = [
      card({ key: 'AER-4', type: 'bug', openQuestions: 1 }),
      card({ key: 'AER-5', type: 'task', openQuestions: 1 }),
    ];

    expect(filterCards(asked, filter({ waiting: true, types: ['bug'] })).map((c) => c.key)).toEqual(['AER-4']);
  });
});

describe('isFiltering', () => {
  it('is false at the default, when every type is drawn', () => {
    expect(isFiltering(DEFAULT_FILTER)).toBe(false);
    expect(isFiltering(filter({ query: '  ' }))).toBe(false);
  });

  it('is true once anything is narrowed', () => {
    expect(isFiltering(filter({ types: ['bug'] }))).toBe(true);
    expect(isFiltering(filter({ query: 'cert' }))).toBe(true);
    expect(isFiltering(filter({ waiting: true }))).toBe(true);
    expect(isFiltering(filter({ assignee: UNASSIGNED }))).toBe(true);
    expect(isFiltering(filter({ assignee: assigneeToken(person('Ada')) }))).toBe(true);
    expect(isFiltering(filter({ project: 'AER' }))).toBe(true);
  });
});

describe('the project facet', () => {
  const board = [
    card({ key: 'AER-1', projectKey: 'AER', type: 'task' }),
    card({ key: 'OPS-1', projectKey: 'OPS', type: 'bug' }),
    card({ key: 'OPS-2', projectKey: 'OPS', type: 'task' }),
  ];

  it('folds cards from other projects', () => {
    expect(filterCards(board, filter({ project: 'OPS' })).map((c) => c.key)).toEqual(['OPS-1', 'OPS-2']);
  });

  it('folds nothing when no project is chosen', () => {
    expect(filterCards(board, filter({ project: '' }))).toHaveLength(3);
  });

  it('ANDs with the type filter rather than instead of it', () => {
    const narrowed = filter({ project: 'OPS', types: ['task'] });
    expect(filterCards(board, narrowed).map((c) => c.key)).toEqual(['OPS-2']);
  });
});

describe('the assignee facet', () => {
  const ada = person('Ada');
  const claude = key('Claude');

  const board = [
    card({ key: 'AER-1', assignee: ada }),
    card({ key: 'AER-2', assignee: claude }),
    card({ key: 'AER-3', assignee: null }),
    card({ key: 'AER-4', assignee: { ...ada, name: 'Ada Lovelace' } }),
  ];

  it('shows the whole board when nobody was chosen', () => {
    expect(filterCards(board, DEFAULT_FILTER)).toHaveLength(4);
  });

  it('draws one identity\'s cards, whatever name they were drawn under', () => {
    // The token is the kind and the id; a rename between reads does not split
    // one person into two facets.
    const mine = filter({ assignee: assigneeToken(ada) });
    expect(filterCards(board, mine).map((c) => c.key)).toEqual(['AER-1', 'AER-4']);
  });

  it('draws only the cards nobody owns', () => {
    expect(filterCards(board, filter({ assignee: UNASSIGNED })).map((c) => c.key)).toEqual(['AER-3']);
  });

  it('ANDs with the other facets, as they AND with each other', () => {
    const bug = card({ key: 'AER-5', type: 'bug', assignee: ada });
    const narrowed = filter({ assignee: assigneeToken(ada), types: ['bug'] });

    expect(filterCards([...board, bug], narrowed).map((c) => c.key)).toEqual(['AER-5']);
  });
});

describe('assigneeFacets', () => {
  it('is the assignees the board actually has, deduped and in picker order', () => {
    const ada = person('Ada');
    const zoe = person('Zoe');
    const claude = key('Claude');

    const facets = assigneeFacets([
      card({ assignee: claude }),
      card({ assignee: zoe }),
      card({ assignee: null }),
      card({ assignee: ada }),
      card({ assignee: ada }),
    ]);

    expect(facets.map((a) => a.name)).toEqual(['Ada', 'Zoe', 'Claude']);
  });

  it('offers nobody at all for a board nobody owns anything on', () => {
    // An "Unassigned" entry is BoardFilters' own, added above these rows:
    // deriving one from cards that have no assignee would be deriving a fact
    // from its own absence.
    expect(assigneeFacets([card(), card()])).toEqual([]);
  });
});

describe('toggleWaiting', () => {
  it('turns the switch on and off again', () => {
    const on = toggleWaiting(DEFAULT_FILTER);
    expect(on.waiting).toBe(true);
    expect(toggleWaiting(on).waiting).toBe(false);
  });

  it('leaves the rest of the filter alone', () => {
    const narrowed = toggleWaiting(filter({ types: ['bug'], query: 'cert' }));
    expect(narrowed.types).toEqual(['bug']);
    expect(narrowed.query).toBe('cert');
  });
});

describe('toggleType', () => {
  it('adds a type that is off and removes one that is on', () => {
    const once = toggleType(filter({ types: ['epic'] }), 'bug');
    expect(once.types).toEqual(['epic', 'bug']);
    expect(toggleType(once, 'bug').types).toEqual(['epic']);
  });

  it('keeps the types in canonical order whatever order they were ticked in', () => {
    expect(toggleType(filter({ types: ['epic', 'story', 'bug'] }), 'task').types).toEqual(['epic', 'story', 'task', 'bug']);
    expect(toggleType(filter({ types: ['bug'] }), 'epic').types).toEqual(['epic', 'bug']);
  });

  it('removes one of four and leaves three', () => {
    expect(toggleType(DEFAULT_FILTER, 'bug').types).toEqual(['epic', 'story', 'task']);
  });

  it('will not remove the only type left, and hands back the same filter', () => {
    const one = filter({ types: ['bug'] });
    expect(toggleType(one, 'bug')).toBe(one);
  });

  it('leaves the rest of the filter alone', () => {
    const next = toggleType(filter({ types: ['bug'], query: 'cert', waiting: true }), 'epic');
    expect(next.query).toBe('cert');
    expect(next.waiting).toBe(true);
  });
});

describe('revealType', () => {
  it('adds a type that is missing, in canonical order', () => {
    expect(revealType(DEFAULT_FILTER, 'task').types).toEqual(['epic', 'story', 'task', 'bug']);
  });

  it('hands back the same filter when the type is already drawn', () => {
    expect(revealType(DEFAULT_FILTER, 'epic')).toBe(DEFAULT_FILTER);
  });

  it('leaves the rest of the filter alone', () => {
    expect(revealType({ ...DEFAULT_FILTER, query: 'cert' }, 'task').query).toBe('cert');
  });
});

describe('typesSummary', () => {
  it('says All for every type and All but for the one missing', () => {
    expect(typesSummary([...ISSUE_TYPES])).toBe('All');
    expect(typesSummary(DEFAULT_FILTER.types)).toBe('All');
    expect(typesSummary(['epic', 'story', 'bug'])).toBe('All but tasks');
    expect(typesSummary(['epic', 'task', 'bug'])).toBe('All but stories');
  });

  it('lists one or two types, plural and capitalised, in canonical order', () => {
    expect(typesSummary(['epic'])).toBe('Epics');
    expect(typesSummary(['story'])).toBe('Stories');
    expect(typesSummary(['task'])).toBe('Tasks');
    expect(typesSummary(['bug'])).toBe('Bugs');
    expect(typesSummary(['epic', 'story'])).toBe('Epics, stories');
    expect(typesSummary(['story', 'task'])).toBe('Stories, tasks');
  });
});

describe('typeCounts', () => {
  const board = [
    card({ key: 'AER-1', type: 'epic' }),
    card({ key: 'AER-2', type: 'task' }),
    card({ key: 'AER-3', type: 'task' }),
  ];

  it('counts every type, and says zero for one with no cards', () => {
    expect(typeCounts(board)).toEqual({ epic: 1, story: 0, task: 2, bug: 0 });
  });

  it('counts the cards a filter would hide', () => {
    expect(typeCounts(board).task).toBe(2);
    expect(filterCards(board, toggleType(DEFAULT_FILTER, 'task'))).toHaveLength(1);
  });
});

describe('readBoardTypes / writeBoardTypes', () => {
  it('defaults to every type when nobody has chosen', () => {
    stubStorage();

    expect(readBoardTypes()).toEqual(DEFAULT_TYPES);
  });

  it('hands back a copy of the default, never the constant itself', () => {
    stubStorage();

    expect(readBoardTypes()).not.toBe(DEFAULT_TYPES);
  });

  it('round-trips a single type, a subset, and all of them', () => {
    stubStorage();

    for (const types of [['task'], ['epic', 'story', 'bug'], [...ISSUE_TYPES]] as (typeof ISSUE_TYPES)[number][][]) {
      writeBoardTypes(types);
      expect(readBoardTypes()).toEqual(types);
    }
  });

  it('stores a JSON array under its own key', () => {
    const data = stubStorage();

    writeBoardTypes(['epic', 'task']);

    expect(data[BOARD_TYPES_STORAGE_KEY]).toBe('["epic","task"]');
  });

  it('returns a stored list in ISSUE_TYPES order', () => {
    stubStorage({ [BOARD_TYPES_STORAGE_KEY]: '["bug","epic"]' });

    expect(readBoardTypes()).toEqual(['epic', 'bug']);
  });

  it('drops types this build does not have and keeps the known ones', () => {
    stubStorage({ [BOARD_TYPES_STORAGE_KEY]: '["initiative","task","spike"]' });

    expect(readBoardTypes()).toEqual(['task']);
  });

  it.each([['an empty list', '[]'], ['only unknowns', '["initiative"]'], ['non-JSON', 'epic,task'], ['a string', '"task"'], ['an object', '{}']])(
    'falls back to the default for %s',
    (_, stored) => {
      stubStorage({ [BOARD_TYPES_STORAGE_KEY]: stored });

      expect(readBoardTypes()).toEqual(DEFAULT_TYPES);
    },
  );

  it('falls back to the default when storage throws', () => {
    stubStorage({}, true);

    expect(readBoardTypes()).toEqual(DEFAULT_TYPES);
  });

  it('does not throw when storage cannot be written', () => {
    stubStorage({}, true);

    expect(() => writeBoardTypes(['task'])).not.toThrow();
  });
});
