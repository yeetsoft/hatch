import { describe, expect, it } from 'vitest';
import {
  ACTOR,
  actorOptions,
  chooseActorWords,
  emptyWords,
  headlineWords,
  INCLUDE_KEYS,
  isPerson,
  kindWords,
  kindsWords,
  resolveActor,
  resolveIncludeKeys,
  touchWords,
  truncatedWords,
  withActor,
  withIncludeKeys,
} from './activity';
import type { Assignee } from '../types';

const person = (name: string): Assignee => ({ kind: 'person', id: `p-${name}`, name });
const key = (name: string): Assignee => ({ kind: 'key', id: `k-${name}`, name });
const params = (query: string) => new URLSearchParams(query);

describe('resolveActor', () => {
  const me = { me: person('Ada') };

  it('takes the URL over the signed-in person', () => {
    expect(resolveActor(params('actor=Zoe'), me)).toEqual({ actor: 'Zoe', fromUrl: true });
  });

  it('falls to the signed-in person with no actor in the URL', () => {
    expect(resolveActor(params(''), me)).toEqual({ actor: 'Ada', fromUrl: false });
  });

  it('counts a blank or whitespace actor as absent', () => {
    expect(resolveActor(params('actor='), me)).toEqual({ actor: 'Ada', fromUrl: false });
    expect(resolveActor(params('actor=%20%20'), me)).toEqual({ actor: 'Ada', fromUrl: false });
  });

  it('is nobody when the URL names nobody and nobody is signed in', () => {
    expect(resolveActor(params(''), { me: null })).toEqual({ actor: null, fromUrl: false });
  });

  it('returns a URL actor verbatim and untrimmed, listed or not', () => {
    expect(resolveActor(params('actor=%20revoked-key%20'), { me: null })).toEqual({
      actor: ' revoked-key ',
      fromUrl: true,
    });
  });
});

describe('withActor', () => {
  it('sets the actor and keeps everything else, without mutating its input', () => {
    const before = params('from=a&to=b&range=2w&project=HA&project=XY');
    const after = withActor(before, 'Ada');

    expect(after.get(ACTOR)).toBe('Ada');
    expect(after.get('from')).toBe('a');
    expect(after.get('to')).toBe('b');
    expect(after.get('range')).toBe('2w');
    expect(after.getAll('project')).toEqual(['HA', 'XY']);
    expect(before.has(ACTOR)).toBe(false);
  });

  it('replaces an actor already there', () => {
    expect(withActor(params('actor=Zoe'), 'Ada').getAll(ACTOR)).toEqual(['Ada']);
  });
});

describe('includeKeys', () => {
  it('is on only when the URL says true', () => {
    expect(resolveIncludeKeys(params(''))).toBe(false);
    expect(resolveIncludeKeys(params('includeKeys=true'))).toBe(true);
    expect(resolveIncludeKeys(params('includeKeys=1'))).toBe(false);
  });

  it('is written when on and removed when off, keeping the rest and not mutating its input', () => {
    const before = params('actor=Ada&range=2w');
    const on = withIncludeKeys(before, true);

    expect(on.get(INCLUDE_KEYS)).toBe('true');
    expect(on.get('actor')).toBe('Ada');
    expect(before.has(INCLUDE_KEYS)).toBe(false);
    expect(withIncludeKeys(on, false).has(INCLUDE_KEYS)).toBe(false);
  });

  it('is offered for a person the directory lists, and for nobody else', () => {
    const directory = { me: null, assignees: [person('Ada'), key('Ada laptop')] };

    expect(isPerson(directory, 'Ada')).toBe(true);
    expect(isPerson(directory, 'Ada laptop')).toBe(false);
    expect(isPerson(directory, 'Gone')).toBe(false);
    expect(isPerson(directory, null)).toBe(false);
  });
});

describe('actorOptions', () => {
  it('lists people before keys, each A to Z, by name', () => {
    const rows = actorOptions([key('Claude'), person('Zoe'), key('Ansible'), person('Ada')], null);

    expect(rows.map((r) => r.text)).toEqual(['Ada', 'Zoe', 'Ansible', 'Claude']);
    expect(rows.map((r) => r.value)).toEqual(['Ada', 'Zoe', 'Ansible', 'Claude']);
  });

  it('collapses a person and a key sharing a name into one row', () => {
    expect(actorOptions([person('Ada'), key('Ada')], null)).toEqual([{ value: 'Ada', text: 'Ada' }]);
  });

  it('appends the current actor when the directory has nobody by that name', () => {
    expect(actorOptions([person('Ada')], 'old-key').map((r) => r.value)).toEqual(['Ada', 'old-key']);
  });

  it('does not repeat the current actor when it is listed', () => {
    expect(actorOptions([person('Ada')], 'Ada')).toHaveLength(1);
  });
});

describe('kindWords', () => {
  it('says one and many', () => {
    expect(kindWords('commented', 1)).toBe('1 comment');
    expect(kindWords('commented', 12)).toBe('12 comments');
    expect(kindWords('status_changed', 9)).toBe('9 moves');
    expect(kindWords('created', 6)).toBe('6 created');
  });

  it('spaces out a kind it has no words for', () => {
    expect(kindWords('some_new_kind', 2)).toBe('2 some new kind');
  });
});

describe('kindsWords', () => {
  it('names every kind in the order given', () => {
    expect(
      kindsWords([
        { kind: 'commented', count: 3 },
        { kind: 'status_changed', count: 1 },
        { kind: 'asked', count: 2 },
        { kind: 'created', count: 1 },
      ]),
    ).toBe('3 comments, 1 move, 2 questions, 1 created');
  });
});

describe('headlineWords', () => {
  const kinds = (...pairs: [string, number][]) => pairs.map(([kind, count]) => ({ kind, count }));

  it('is the total and the top kinds', () => {
    expect(
      headlineWords({
        total: 27,
        kinds: kinds(['commented', 12], ['status_changed', 9], ['created', 6]),
      }),
    ).toBe('27 events: 12 comments, 9 moves, 6 created');
  });

  it('folds kinds past the third into one tail counting events', () => {
    expect(
      headlineWords({
        total: 41,
        kinds: kinds(['commented', 12], ['status_changed', 9], ['created', 6], ['asked', 8], ['answered', 6]),
      }),
    ).toBe('41 events: 12 comments, 9 moves, 6 created, and 14 others');
  });

  it('says other in the singular for a tail of one event', () => {
    expect(
      headlineWords({
        total: 4,
        kinds: kinds(['commented', 1], ['status_changed', 1], ['created', 1], ['asked', 1]),
      }),
    ).toBe('4 events: 1 comment, 1 move, 1 created, and 1 other');
  });

  it('says event in the singular', () => {
    expect(headlineWords({ total: 1, kinds: kinds(['commented', 1]) })).toBe('1 event: 1 comment');
  });

  it('is the total alone with no kinds', () => {
    expect(headlineWords({ total: 0, kinds: [] })).toBe('0 events');
  });
});

describe('the sentences', () => {
  it('names the actor in the empty range', () => {
    expect(emptyWords('Ada')).toBe('Nothing recorded for Ada in this window.');
  });

  it('tells the reader to choose', () => {
    expect(chooseActorWords()).toBe('Choose whose activity to read.');
  });

  it('says how much of the trail is shown', () => {
    expect(truncatedWords({ total: 612, events: new Array(500).fill(null) })).toBe(
      'Showing the newest 500 of 612 events; the rundown above covers all of them.',
    );
  });
});

describe('touchWords', () => {
  it('is the instant on the UTC clock at no offset', () => {
    expect(touchWords('2026-11-04T14:05:00.000Z', 0, 'en-GB')).toBe('4 Nov 2026, 14:05');
  });

  it('moves onto the reader\'s clock, across midnight into the next day', () => {
    expect(touchWords('2026-11-04T23:30:00.000Z', 120, 'en-GB')).toBe('5 Nov 2026, 01:30');
  });

  it('moves west across midnight into the day before', () => {
    expect(touchWords('2026-11-04T01:00:00.000Z', -300, 'en-GB')).toBe('3 Nov 2026, 20:00');
  });
});
