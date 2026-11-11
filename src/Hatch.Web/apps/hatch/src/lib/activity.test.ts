import { describe, expect, it } from 'vitest';
import {
  ACTOR,
  actorOptions,
  chooseActorWords,
  emptyWords,
  estimateHeadlineWords,
  estimateRuleWords,
  GAP,
  headlineWords,
  isValidGap,
  isValidLead,
  kindWords,
  kindsWords,
  LEAD,
  resolveActor,
  resolveGap,
  resolveLead,
  sessionLengthWords,
  sessionWhenWords,
  touchWords,
  truncatedWords,
  withActor,
  withGap,
  withLead,
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

describe('resolveGap', () => {
  it('defaults with no gap in the URL', () => {
    expect(resolveGap(params(''))).toBe(30);
  });

  it('takes the integer in range', () => {
    expect(resolveGap(params('gap=45'))).toBe(45);
  });

  it('defaults rather than refuses when out of range or not a number', () => {
    expect(resolveGap(params('gap=0'))).toBe(30);
    expect(resolveGap(params('gap=1441'))).toBe(30);
    expect(resolveGap(params('gap=soon'))).toBe(30);
    expect(resolveGap(params('gap=12.5'))).toBe(30);
  });
});

describe('resolveLead', () => {
  it('defaults with no lead in the URL', () => {
    expect(resolveLead(params(''))).toBe(15);
  });

  it('takes the integer in range, including the bottom of it', () => {
    expect(resolveLead(params('lead=20'))).toBe(20);
    expect(resolveLead(params('lead=0'))).toBe(0);
  });

  it('defaults rather than refuses when out of range or not a number', () => {
    expect(resolveLead(params('lead=-1'))).toBe(15);
    expect(resolveLead(params('lead=241'))).toBe(15);
    expect(resolveLead(params('lead=never'))).toBe(15);
  });
});

describe('isValidGap and isValidLead', () => {
  it('accepts the integer in range, rejects anything else', () => {
    expect(isValidGap(1)).toBe(true);
    expect(isValidGap(1440)).toBe(true);
    expect(isValidGap(0)).toBe(false);
    expect(isValidGap(1441)).toBe(false);
    expect(isValidGap(12.5)).toBe(false);
    expect(isValidGap(NaN)).toBe(false);
  });

  it('allows a lead of zero, which a gap may not be', () => {
    expect(isValidLead(0)).toBe(true);
    expect(isValidGap(0)).toBe(false);
    expect(isValidLead(240)).toBe(true);
    expect(isValidLead(241)).toBe(false);
  });
});

describe('withGap and withLead', () => {
  it('set the one param and keep everything else, without mutating the input', () => {
    const before = params('actor=Ada&gap=30');
    const after = withGap(before, 45);

    expect(after.get(GAP)).toBe('45');
    expect(after.get('actor')).toBe('Ada');
    expect(before.get(GAP)).toBe('30');
  });

  it('sets the lead', () => {
    expect(withLead(params('lead=15'), 20).get(LEAD)).toBe('20');
  });
});

describe('estimateHeadlineWords', () => {
  it('names the sessions and the hours', () => {
    expect(estimateHeadlineWords({ sessions: 3, minutes: 150 })).toBe('3 sessions, about 2.5 hours in this window');
  });

  it('says session and hour in the singular', () => {
    expect(estimateHeadlineWords({ sessions: 1, minutes: 45 })).toBe('1 session, about 0.8 hour in this window');
    expect(estimateHeadlineWords({ sessions: 2, minutes: 60 })).toBe('2 sessions, about 1.0 hour in this window');
  });
});

describe('estimateRuleWords', () => {
  it('states the rule from the estimate\'s own gap and lead', () => {
    expect(estimateRuleWords({ gapMinutes: 30, leadMinutes: 15 })).toBe(
      'A session ends after 30 minutes of quiet; each is counted from its first event to its last, ' +
        'plus 15 minutes.',
    );
  });

  it('says minute in the singular', () => {
    expect(estimateRuleWords({ gapMinutes: 1, leadMinutes: 1 })).toBe(
      'A session ends after 1 minute of quiet; each is counted from its first event to its last, plus 1 minute.',
    );
  });
});

describe('sessionLengthWords', () => {
  it('is minutes alone under an hour', () => {
    expect(sessionLengthWords(15)).toBe('15m');
  });

  it('is whole hours with none left over', () => {
    expect(sessionLengthWords(120)).toBe('2h');
  });

  it('is hours and minutes', () => {
    expect(sessionLengthWords(90)).toBe('1h30m');
  });
});

describe('sessionWhenWords', () => {
  it('names the date once on the same calendar day', () => {
    expect(
      sessionWhenWords(
        { from: '2026-11-04T09:00:00.000Z', to: '2026-11-04T10:30:00.000Z' },
        0,
        'en-GB',
      ),
    ).toBe('4 Nov 2026, 09:00–10:30');
  });

  it('names both dates when the offset carries the session into the next day', () => {
    expect(
      sessionWhenWords(
        { from: '2026-11-04T23:30:00.000Z', to: '2026-11-05T00:15:00.000Z' },
        0,
        'en-GB',
      ),
    ).toBe('4 Nov 2026, 23:30 – 5 Nov 2026, 00:15');
  });
});
