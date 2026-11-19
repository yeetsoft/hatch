import { describe, expect, it } from 'vitest';
import {
  DEFAULT_PRESET,
  PODIUM,
  capPhrase,
  dayLabel,
  droppedRowLabel,
  emptiness,
  foldDropped,
  inScopeCards,
  parseOutcome,
  presetRange,
  rankedTop,
  rankingCaption,
  reasonPhrase,
  resolveQuery,
  sessionWindow,
  windowPhrase,
} from './leaderboard';
import type { IssueCard, WorkLogByPlaybookRow, WorkLogSession, WorkLogSessions, WorkLogTotals } from '../types';

const totals = (over: Partial<WorkLogTotals> = {}): WorkLogTotals => ({
  sessions: 31,
  errors: 0,
  inputTokens: 900,
  outputTokens: 280,
  cacheCreationTokens: 3_200,
  cacheReadTokens: 59_000,
  totalTokens: 63_380,
  costUsd: 3.41,
  unpricedSessions: 0,
  durationMs: 842_000,
  ...over,
});

const session = (over: Partial<WorkLogSession> = {}): WorkLogSession => ({
  id: 1,
  sessionId: '3d1abf4f-aaaa-4000-8000-000000000001',
  agent: 'claude',
  issueKey: 'AER-12',
  issueTitle: 'The leaderboard query',
  startedAt: '2026-09-07T02:40:00Z',
  endedAt: '2026-09-07T02:54:02Z',
  durationMs: 842_000,
  title: 'Broke the ranking out of the rollup',
  described: true,
  isError: false,
  turns: 41,
  costUsd: 3.41,
  inputTokens: 900,
  outputTokens: 280,
  cacheCreationTokens: 3_200,
  cacheReadTokens: 59_000,
  totalTokens: 63_380,
  requests: 42,
  peakContextTokens: 140_000,
  promptChars: 16_533,
  dispatchModel: null,
  modelPlacement: null,
  modelDigest: null,
  outcome: null,
  reason: null,
  ...over,
});

const view = (over: Partial<WorkLogSessions> = {}): WorkLogSessions => ({
  from: '2026-08-24T00:00:00Z',
  to: '2026-09-08T00:00:00Z',
  sort: 'tokens',
  totals: totals(),
  firstSessionAt: '2026-08-19T12:00:00Z',
  lastSessionAt: '2026-09-07T12:00:00Z',
  sessions: [session()],
  ...over,
});

/* Midday UTC throughout, so no assertion straddles a date boundary in a zone a
   test might be run in. */
const now = new Date('2026-09-07T12:34:00Z');

describe('presetRange', () => {
  it('ends at the first grid boundary strictly after now', () => {
    // The last bucket is the one running now, drawn as far as it has got.
    expect(presetRange('24h', now, 0).to).toBe('2026-09-07T13:00:00.000Z');
    expect(presetRange('14d', now, 0).to).toBe('2026-09-08T00:00:00.000Z');
  });

  it('still gives a whole bucket when now is exactly on a boundary', () => {
    const onTheHour = new Date('2026-09-07T13:00:00Z');

    expect(presetRange('24h', onTheHour, 0).to).toBe('2026-09-07T14:00:00.000Z');
  });

  it('reaches back the span each preset names', () => {
    expect(presetRange('24h', now, 0).from).toBe('2026-09-06T13:00:00.000Z');
    expect(presetRange('today', now, 0).from).toBe('2026-09-07T00:00:00.000Z');
    expect(presetRange('7d', now, 0).from).toBe('2026-09-01T00:00:00.000Z');
    expect(presetRange('14d', now, 0).from).toBe('2026-08-25T00:00:00.000Z');
    expect(presetRange('30d', now, 0).from).toBe('2026-08-09T00:00:00.000Z');
  });

  it("shifts the daily grid to the reader's midnight", () => {
    // Five hours west: the boundary is 05:00Z, which is midnight there.
    const range = presetRange('today', now, -300);

    expect(range.from).toBe('2026-09-07T05:00:00.000Z');
    expect(range.to).toBe('2026-09-08T05:00:00.000Z');
  });

  it('leaves the hourly grid alone, as the server does', () => {
    // Every whole-hour zone lands on the same hourly grid anyway, and a
    // half-hour zone gets a label half an hour off rather than a wrong answer.
    expect(presetRange('24h', now, -300)).toEqual(presetRange('24h', now, 0));
  });
});

describe('resolveQuery', () => {
  it('opens on the default preset and sort with nothing in the URL', () => {
    const query = resolveQuery(new URLSearchParams(), now, 0);

    expect(query.preset).toBe(DEFAULT_PRESET);
    expect(query.sort).toBe('tokens');
    expect(query.measure).toBe('tokens');
    expect(query.issue).toBeNull();
    expect(query.outcome).toBe('any');
    expect(query).toMatchObject(presetRange(DEFAULT_PRESET, now, 0));
  });

  it('falls back rather than refusing what it does not recognise', () => {
    const query = resolveQuery(
      new URLSearchParams({ range: 'fortnight', sort: 'turns', measure: 'turns', outcome: 'finished' }),
      now,
      0,
    );

    expect(query.preset).toBe(DEFAULT_PRESET);
    expect(query.sort).toBe('tokens');
    expect(query.measure).toBe('tokens');
    expect(query.outcome).toBe('any');
  });

  it('takes a recognised outcome as written', () => {
    expect(resolveQuery(new URLSearchParams({ outcome: 'worked' }), now, 0).outcome).toBe('worked');
    expect(resolveQuery(new URLSearchParams({ outcome: 'dropped' }), now, 0).outcome).toBe('dropped');
    expect(resolveQuery(new URLSearchParams({ outcome: 'none' }), now, 0).outcome).toBe('none');
  });

  it('takes the sort, the measure and the issue filter as written', () => {
    const query = resolveQuery(new URLSearchParams({ sort: 'cost', measure: 'cost', issue: 'AER-12' }), now, 0);

    expect(query.sort).toBe('cost');
    // The graph's choice is state like any other: a pasted link that opens on
    // dollars is the same argument as a pasted sort.
    expect(query.measure).toBe('cost');
    expect(query.issue).toBe('AER-12');
  });

  it('takes requests and peakContext as sorts', () => {
    expect(resolveQuery(new URLSearchParams({ sort: 'requests' }), now, 0).sort).toBe('requests');
    expect(resolveQuery(new URLSearchParams({ sort: 'peakContext' }), now, 0).sort).toBe('peakContext');
  });

  it('lets an explicit window beat the preset and passes it through as typed', () => {
    const query = resolveQuery(
      new URLSearchParams({ range: '7d', from: '2026-01-01T09:15:00Z', to: '2026-01-02T09:15:00Z' }),
      now,
      0,
    );

    // A paste is honoured exactly, off-grid or not, and no preset is pressed.
    expect(query.from).toBe('2026-01-01T09:15:00Z');
    expect(query.to).toBe('2026-01-02T09:15:00Z');
    expect(query.preset).toBeNull();
  });

  it('ignores half a window', () => {
    const query = resolveQuery(new URLSearchParams({ from: '2026-01-01T09:15:00Z' }), now, 0);

    expect(query.preset).toBe(DEFAULT_PRESET);
    expect(query.from).toBe(presetRange(DEFAULT_PRESET, now, 0).from);
  });
});

describe('rankedTop', () => {
  it('takes five from a longer list and all of a shorter one', () => {
    const many = Array.from({ length: 9 }, (_, i) => session({ id: i }));

    expect(rankedTop(many)).toHaveLength(PODIUM);
    expect(rankedTop([session({ id: 1 }), session({ id: 2 })])).toHaveLength(2);
  });

  it('never re-orders what the server ranked', () => {
    const given = [session({ id: 1, totalTokens: 10 }), session({ id: 2, totalTokens: 900_000 })];

    expect(rankedTop(given).map((s) => s.id)).toEqual([1, 2]);
  });
});

describe('the sentences the ranking says', () => {
  it('counts the podium against the whole window', () => {
    expect(rankingCaption(totals({ sessions: 31 }), 5)).toBe(
      'The 5 most expensive sessions of the 31 that ran in this window.',
    );
  });

  it('says session, singular, when there is only one', () => {
    expect(rankingCaption(totals({ sessions: 1 }), 1)).toBe(
      'The most expensive session of the 1 that ran in this window.',
    );
  });
});

describe('capPhrase', () => {
  it('says how many of how many when the filter holds more than the table drew', () => {
    expect(capPhrase(totals({ sessions: 214 }), 100)).toBe(
      'Showing the top 100 of 214 sessions; the totals above are for all of them.',
    );
  });

  it('says nothing when every row is on screen', () => {
    expect(capPhrase(totals({ sessions: 31 }), 31)).toBeNull();
    expect(capPhrase(totals({ sessions: 4 }), 100)).toBeNull();
  });
});

describe('emptiness', () => {
  it('says nothing at all when there are rows', () => {
    expect(emptiness(view(), null)).toBeNull();
  });

  it('tells a log that has never been written from a window nobody worked', () => {
    const never = emptiness(view({ firstSessionAt: null, lastSessionAt: null, totals: totals({ sessions: 0 }) }), null);
    const empty = emptiness(view({ totals: totals({ sessions: 0 }) }), null, 'en-GB');

    // firstSessionAt tells them apart, not the row count - and `never` wins
    // when both could speak.
    expect(never?.kind).toBe('never');
    expect(never?.text).toContain('Nothing has ever been logged here');
    expect(empty?.kind).toBe('range');
    expect(empty?.text).toBe('No session ran in this window. The log runs from 19 Aug to 7 Sept.');
  });

  it('names the issue filter in the never-logged sentence', () => {
    const scoped = emptiness(view({ firstSessionAt: null, lastSessionAt: null }), 'AER-12');

    expect(scoped?.text).toContain('under AER-12');
  });
});

describe('sessionWindow', () => {
  const query = { from: '2026-08-24T00:00:00Z', to: '2026-09-08T00:00:00Z', issue: null };

  it('leaves projectId out when the scope has no ids', () => {
    expect(sessionWindow(query, [])).toEqual({
      from: query.from,
      to: query.to,
      ancestorKey: null,
    });
  });

  it('names projectId when the scope has ids, and keeps the rest', () => {
    expect(sessionWindow({ ...query, issue: 'AER-12' }, [2, 5])).toEqual({
      from: query.from,
      to: query.to,
      ancestorKey: 'AER-12',
      projectId: [2, 5],
    });
  });

  it('leaves outcome out at any, and names it otherwise', () => {
    expect(sessionWindow({ ...query, outcome: 'any' }, [])).toEqual({
      from: query.from,
      to: query.to,
      ancestorKey: null,
    });

    expect(sessionWindow({ ...query, outcome: 'dropped' }, [])).toEqual({
      from: query.from,
      to: query.to,
      ancestorKey: null,
      outcome: 'dropped',
    });
  });
});

describe('parseOutcome', () => {
  it('falls back to any for what it does not recognise', () => {
    expect(parseOutcome(null)).toBe('any');
    expect(parseOutcome('')).toBe('any');
    expect(parseOutcome('finished')).toBe('any');
  });

  it('takes a recognised value as written', () => {
    expect(parseOutcome('worked')).toBe('worked');
    expect(parseOutcome('dropped')).toBe('dropped');
    expect(parseOutcome('none')).toBe('none');
  });
});

describe('reasonPhrase', () => {
  it('reads the three stall reasons in words', () => {
    expect(reasonPhrase('quiet')).toBe('quiet limit');
    expect(reasonPhrase('run-time')).toBe('run-time limit');
    expect(reasonPhrase('error')).toBe('error');
  });

  it('reads no reason as plain moved-nothing, and an unknown one as itself', () => {
    expect(reasonPhrase(null)).toBe('moved nothing');
    expect(reasonPhrase('expired')).toBe('expired');
  });
});

describe('foldDropped and droppedRowLabel', () => {
  const byPlaybookRow = (over: Partial<WorkLogByPlaybookRow> = {}): WorkLogByPlaybookRow => ({
    playbookKey: '7',
    fromStatusId: 1,
    fromStatusName: 'To Do',
    toStatusId: 2,
    toStatusName: 'Doing',
    issueType: 'task',
    model: 'sonnet',
    effort: 'high',
    outcome: 'dropped',
    reason: 'quiet',
    sessions: 1,
    errors: 0,
    undescribed: 0,
    turns: 1,
    inputTokens: 100,
    outputTokens: 0,
    cacheCreationTokens: 0,
    cacheReadTokens: 0,
    totalTokens: 100,
    costUsd: 1,
    unpricedSessions: 0,
    medianPeakContextTokens: null,
    maxPeakContextTokens: null,
    medianPromptChars: null,
    ...over,
  });

  it('groups by playbook, transition and reason, ignoring type, model and effort', () => {
    const rows = [
      byPlaybookRow({ model: 'sonnet', totalTokens: 100, sessions: 1, costUsd: 1 }),
      byPlaybookRow({ model: 'opus', totalTokens: 50, sessions: 2, costUsd: 2 }),
    ];

    const folded = foldDropped(rows);

    expect(folded).toHaveLength(1);
    expect(folded[0].sessions).toBe(3);
    expect(folded[0].totalTokens).toBe(150);
    expect(folded[0].costUsd).toBe(3);
  });

  it('keeps a different reason or transition as its own row, heaviest first', () => {
    const rows = [
      byPlaybookRow({ reason: 'quiet', totalTokens: 100 }),
      byPlaybookRow({ reason: 'run-time', totalTokens: 400 }),
      byPlaybookRow({ toStatusId: 3, toStatusName: 'Done', reason: 'quiet', totalTokens: 200 }),
    ];

    const folded = foldDropped(rows);

    expect(folded.map((r) => r.totalTokens)).toEqual([400, 200, 100]);
  });

  it("the folded cost equals the sum of what went in", () => {
    const rows = [
      byPlaybookRow({ costUsd: 1.5, totalTokens: 100 }),
      byPlaybookRow({ reason: 'run-time', costUsd: 2.25, totalTokens: 50 }),
    ];

    const total = rows.reduce((sum, r) => sum + r.costUsd, 0);

    expect(foldDropped(rows).reduce((sum, r) => sum + r.costUsd, 0)).toBe(total);
  });

  it('folds every all-null row an older runner wrote into one group, labelled unknown', () => {
    const rows = [
      byPlaybookRow({ playbookKey: null, fromStatusId: null, fromStatusName: null, toStatusId: null, toStatusName: null, reason: null, totalTokens: 10 }),
      byPlaybookRow({ playbookKey: null, fromStatusId: null, fromStatusName: null, toStatusId: null, toStatusName: null, reason: null, totalTokens: 20 }),
    ];

    const folded = foldDropped(rows);

    expect(folded).toHaveLength(1);
    expect(folded[0].totalTokens).toBe(30);
    expect(droppedRowLabel(folded[0])).toBe('Unknown playbook');
  });

  it('labels an ordinary row by its transition, falling back to the id for a deleted column', () => {
    const row = foldDropped([byPlaybookRow()])[0];
    expect(droppedRowLabel(row)).toBe('To Do → Doing');

    const gone = foldDropped([byPlaybookRow({ toStatusId: 9, toStatusName: null })])[0];
    expect(droppedRowLabel(gone)).toBe('To Do → 9');
  });
});

describe('inScopeCards', () => {
  const card = (key: string, projectKey: string): IssueCard => ({
    key,
    projectKey,
    type: 'task',
    title: `${key} title`,
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
    preApproval: 'none',
    preApprovalOwn: 'none',
    preApprovalFrom: null,
    localModel: 'none',
    localModelOwn: 'none',
    localModelFrom: null,
    humanWork: 'agent',
    humanWorkOwn: 'inherit',
    humanWorkFrom: null,
    runnerTags: [],
    runnerTagsOwn: [],
    runnerTagsFrom: [],
    dangerousOwn: null,
    dangerous: null,
    dangerousFrom: null,
    omittedRunnersOwn: [],
    omittedRunners: [],
    express: false,
    stalledAt: null,
    stalledWhy: null,
    held: false,
    pullRequestUrl: null,
    onePr: false,
    onePrOwn: null,
    onePrFrom: null,
    createdBy: 'Britta Perry',
    projectPriority: 'normal',
  });

  const cards = [card('AER-1', 'AER'), card('FIL-1', 'FIL'), card('AER-2', 'AER')];

  it('keeps only the cards inScope admits, in order', () => {
    expect(inScopeCards(cards, (projectKey) => projectKey === 'AER').map((c) => c.key)).toEqual([
      'AER-1',
      'AER-2',
    ]);
  });

  it('returns every card when inScope admits everything', () => {
    expect(inScopeCards(cards, () => true)).toEqual(cards);
  });
});

describe('the labels', () => {
  it('says a day short and a window as two of them', () => {
    expect(dayLabel('2026-08-19T12:00:00Z', 'en-GB')).toBe('19 Aug');
    expect(windowPhrase('2026-08-24T12:00:00Z', '2026-09-07T12:00:00Z', 'en-GB')).toBe('24 Aug – 7 Sept');
  });
});
