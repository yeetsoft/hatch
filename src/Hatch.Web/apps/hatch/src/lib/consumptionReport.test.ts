import { describe, expect, it } from 'vitest';
import {
  DEFAULT_SORT,
  DEFAULT_TYPE,
  SORT,
  STATUS,
  TABLE_LIMIT,
  TYPE,
  capWords,
  consumptionCsvHref,
  consumptionFilterQuery,
  emptiness,
  humanDuration,
  readSearch,
  resolveMeasure,
  resolveSort,
  resolveStatusId,
  resolveType,
  rowCost,
  totalsLine,
  withMeasure,
  withSort,
  withStatusId,
  withType,
  type ConsumptionFilterQuery,
} from './consumptionReport';
import { MEASURE } from './leaderboard';
import { UNPRICED_TITLE } from './workLog';
import type { ConsumptionReport, WorkLogTotals } from '../types';

const totals = (over: Partial<WorkLogTotals> = {}): WorkLogTotals => ({
  sessions: 4,
  errors: 0,
  inputTokens: 900,
  outputTokens: 280,
  cacheCreationTokens: 3_200,
  cacheReadTokens: 59_000,
  totalTokens: 1_400_000,
  costUsd: 3.41,
  unpricedSessions: 0,
  durationMs: 11_520_000,
  ...over,
});

const report = (over: Partial<ConsumptionReport> = {}): ConsumptionReport => ({
  from: '2026-08-01T00:00:00.000Z',
  to: '2026-11-01T00:00:00.000Z',
  type: 'epic',
  sort: 'activity',
  limit: TABLE_LIMIT,
  matched: 12,
  totals: totals(),
  firstSessionAt: '2026-08-19T10:00:00.000Z',
  lastSessionAt: '2026-10-07T10:00:00.000Z',
  rows: [],
  history: {
    from: '2026-08-01T00:00:00.000Z',
    to: '2026-11-01T00:00:00.000Z',
    bucket: 'day',
    totals: totals(),
    firstSessionAt: '2026-08-19T10:00:00.000Z',
    lastSessionAt: '2026-10-07T10:00:00.000Z',
    buckets: [],
  },
  ...over,
});

const query = (text: string) => new URLSearchParams(text);

describe('type', () => {
  it('defaults to epic and writes nothing at the default', () => {
    expect(resolveType(query(''))).toBe(DEFAULT_TYPE);
    expect(withType(query('type=story&range=3m'), 'epic').toString()).toBe('range=3m');
  });

  it.each(['epic', 'reflection', 'story', 'task', 'bug', 'all'] as const)('round-trips %s', (type) => {
    expect(resolveType(withType(query(''), type))).toBe(type);
  });

  it('writes a non-default type and keeps the rest of the address', () => {
    expect(withType(query('range=3m'), 'all').toString()).toBe(`range=3m&${TYPE}=all`);
  });

  it('reads a typo, a case slip or stray space as it can', () => {
    expect(resolveType(query('type=banana'))).toBe('epic');
    expect(resolveType(query('type=%20Bug%20'))).toBe('bug');
  });

  it('never mutates its input', () => {
    const params = query('type=bug');
    withType(params, 'epic');
    expect(params.toString()).toBe('type=bug');
  });
});

describe('sort', () => {
  it('defaults to activity and writes nothing at the default', () => {
    expect(resolveSort(query(''))).toBe(DEFAULT_SORT);
    expect(withSort(query('sort=tokens&type=all'), 'activity').toString()).toBe('type=all');
  });

  it.each(['activity', 'tokens', 'cost', 'time'] as const)('round-trips %s', (sort) => {
    expect(resolveSort(withSort(query(''), sort))).toBe(sort);
  });

  it('writes a non-default sort and falls back on a typo', () => {
    expect(withSort(query(''), 'cost').toString()).toBe(`${SORT}=cost`);
    expect(resolveSort(query('sort=sessions'))).toBe('activity');
  });
});

describe('measure', () => {
  it('defaults to tokens and writes nothing at the default', () => {
    expect(resolveMeasure(query(''))).toBe('tokens');
    expect(withMeasure(query('measure=cost&range=3m'), 'tokens').toString()).toBe('range=3m');
  });

  it('reads cost, and takes junk and a case slip as the default', () => {
    expect(resolveMeasure(query('measure=cost'))).toBe('cost');
    expect(resolveMeasure(query('measure=banana'))).toBe('tokens');
    expect(resolveMeasure(query('measure=COST'))).toBe('tokens');
  });

  it('writes a non-default measure and keeps the rest of the address', () => {
    expect(withMeasure(query('type=all'), 'cost').toString()).toBe(`type=all&${MEASURE}=cost`);
  });

  it('never mutates its input', () => {
    const params = query('measure=cost');
    withMeasure(params, 'tokens');
    expect(params.toString()).toBe('measure=cost');
  });
});

describe('readSearch', () => {
  it('is the same before and after the measure moves, so the report is not refetched', () => {
    const before = query('type=all&range=3m');

    expect(readSearch(withMeasure(before, 'cost'))).toBe(readSearch(before));
    expect(readSearch(withMeasure(before, 'cost'))).toBe('type=all&range=3m');
  });

  it('changes when anything the read depends on does', () => {
    const before = query('range=3m');

    expect(readSearch(withType(before, 'task'))).not.toBe(readSearch(before));
    expect(readSearch(withSort(before, 'cost'))).not.toBe(readSearch(before));
  });

  it('does not mutate its input', () => {
    const params = query('measure=cost');
    readSearch(params);
    expect(params.toString()).toBe('measure=cost');
  });
});

describe('status', () => {
  it('is every column when absent, unknown or not a number', () => {
    expect(resolveStatusId(query(''))).toBeNull();
    expect(resolveStatusId(query('status=done'))).toBeNull();
    expect(resolveStatusId(query('status=-3'))).toBeNull();
    expect(resolveStatusId(query('status=0'))).toBeNull();
    expect(resolveStatusId(query('status=2.5'))).toBeNull();
  });

  it('round-trips a column id as text', () => {
    expect(withStatusId(query('range=3m'), 3).toString()).toBe(`range=3m&${STATUS}=3`);
    expect(resolveStatusId(withStatusId(query(''), 3))).toBe(3);
  });

  it('writes nothing for every column', () => {
    expect(withStatusId(query('status=3&type=all'), null).toString()).toBe('type=all');
  });
});

describe('consumptionFilterQuery', () => {
  const full: ConsumptionFilterQuery = {
    from: '2026-10-01T00:00:00.000Z',
    to: '2026-11-01T00:00:00.000Z',
    projectId: [3, 5],
    statusId: [7],
    type: 'story',
    sort: 'cost',
    offsetMinutes: -300,
  };

  it('sends every field, repeating projectId and statusId once per id', () => {
    expect(consumptionFilterQuery(full).toString()).toBe(
      'from=2026-10-01T00%3A00%3A00.000Z&to=2026-11-01T00%3A00%3A00.000Z' +
        '&projectId=3&projectId=5&statusId=7&type=story&sort=cost&offsetMinutes=-300',
    );
  });

  it('leaves off an empty from/to, an absent type/sort, and empty id lists', () => {
    expect(
      consumptionFilterQuery({
        from: '',
        to: null,
        projectId: [],
        statusId: [],
        type: null,
        sort: null,
        offsetMinutes: 0,
      }).toString(),
    ).toBe('offsetMinutes=0');
  });

  it('always sends offsetMinutes', () => {
    expect(consumptionFilterQuery({ offsetMinutes: 60 }).toString()).toBe('offsetMinutes=60');
  });
});

describe('consumptionCsvHref', () => {
  it('points at the CSV route with the filter query', () => {
    const query: ConsumptionFilterQuery = { type: 'bug', sort: 'tokens', offsetMinutes: 0 };

    expect(consumptionCsvHref(query)).toBe(
      `/api/reports/consumption.csv?${consumptionFilterQuery(query).toString()}`,
    );
  });

  it('builds its query from the same function the JSON read uses', () => {
    const query: ConsumptionFilterQuery = {
      from: '2026-10-01T00:00:00.000Z',
      projectId: [1, 2],
      statusId: [9],
      offsetMinutes: -60,
    };
    const href = consumptionCsvHref(query);

    expect(href.split('?')[1]).toBe(consumptionFilterQuery(query).toString());
  });
});

describe('TABLE_LIMIT', () => {
  it('is a hundred', () => expect(TABLE_LIMIT).toBe(100));
});

describe('humanDuration', () => {
  it('says seconds under a minute', () => {
    expect(humanDuration(48_000)).toBe('48s');
    expect(humanDuration(59_999)).toBe('59s');
  });

  it('says minutes and zero-padded seconds under an hour', () => {
    expect(humanDuration(60_000)).toBe('1m 00s');
    expect(humanDuration(303_000)).toBe('5m 03s');
  });

  it('says hours and zero-padded minutes from an hour', () => {
    expect(humanDuration(11_520_000)).toBe('3h 12m');
    expect(humanDuration(3_600_000 + 5 * 60_000)).toBe('1h 05m');
  });

  it('says 0s for nothing and for what is not a duration', () => {
    expect(humanDuration(0)).toBe('0s');
    expect(humanDuration(-5)).toBe('0s');
    expect(humanDuration(Number.NaN)).toBe('0s');
  });
});

describe('rowCost', () => {
  it('is a dash with the unpriced title when no session was priced', () => {
    expect(rowCost(totals({ sessions: 3, unpricedSessions: 3, costUsd: 0 }))).toEqual({
      text: '—',
      title: UNPRICED_TITLE,
    });
  });

  it('is the sum alone when every session was priced', () => {
    expect(rowCost(totals())).toEqual({ text: '$3.41' });
  });

  it('is the floor with the count beside it when some were not', () => {
    expect(rowCost(totals({ sessions: 4, unpricedSessions: 1 }))).toEqual({ text: '$3.41 (1 unpriced)' });
  });
});

describe('totalsLine', () => {
  it('says issues, then the leaderboard totals, then runner time', () => {
    expect(totalsLine(report())).toBe('12 issues · 4 sessions · 1.4M tokens · $3.41 · 3h 12m');
  });
});

describe('capWords', () => {
  it('is null when every match is drawn', () => {
    expect(capWords(100, 100)).toBeNull();
    expect(capWords(3, 100)).toBeNull();
  });

  it('says how many of how many, and that the totals cover all of them', () => {
    expect(capWords(240, 100)).toBe('The first 100 of 240 issues. The totals cover all 240.');
  });
});

describe('emptiness', () => {
  it('is never when nothing was ever logged under the filter', () => {
    expect(emptiness(report({ firstSessionAt: null, lastSessionAt: null, totals: totals({ sessions: 0 }) }))).toEqual({
      kind: 'never',
      text: 'Nothing has ever been logged under this filter.',
    });
  });

  it('is range when the log has sessions but none in the window', () => {
    expect(emptiness(report({ totals: totals({ sessions: 0 }) }), 'en-US')).toEqual({
      kind: 'range',
      text: 'No issue consumed anything in this window. The log runs from Aug 19 to Oct 7.',
    });
  });

  it('is null when the window has sessions', () => {
    expect(emptiness(report())).toBeNull();
  });
});
