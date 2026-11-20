import { describe, expect, it } from 'vitest';
import {
  UNDESCRIBED_TITLE,
  bugColumnName,
  closedFindings,
  evidenceLines,
  findingLasted,
  findingTimeWords,
  findingsOpenAt,
  gooseStateWords,
  noClosedFindingsWords,
  noOpenFindingsWords,
  noRunsWords,
  openFindings,
  runRowWords,
  runsCoverWholeBoard,
  runsCoverWholeBoardWords,
  runsNewestFirst,
  sessionReports,
  sweepCount,
  sweepWords,
} from './goose';
import { gooseStoppedWords } from './attention';
import type { AttentionGoose, GooseFinding, GooseRun, IssueEvent, Status, WorkLogEntry } from '../types';

const NOW = new Date('2026-10-18T12:00:00Z');
const ago = (minutes: number): string => new Date(NOW.getTime() - minutes * 60_000).toISOString();

const goose = (over: Partial<AttentionGoose> = {}): AttentionGoose => ({
  lastSweepAt: ago(2),
  intervalSeconds: 60,
  stopped: false,
  ...over,
});

const run = (over: Partial<GooseRun> = {}): GooseRun => ({
  id: 1,
  firstRanAt: ago(180),
  lastRanAt: ago(2),
  sweeps: 12,
  longestMs: 1200,
  checks: 7,
  checksFailed: 0,
  ...over,
});

const finding = (over: Partial<GooseFinding> = {}): GooseFinding => ({
  id: 1,
  check: 'train-ready',
  subject: 'AER',
  projectKey: 'AER',
  sentence: 'AER has stood ready for a while.',
  evidence: [],
  beganAt: ago(120),
  openedAt: ago(60),
  closedAt: null,
  bugIssueKey: null,
  ...over,
});

const entry = (over: Partial<WorkLogEntry> = {}): WorkLogEntry =>
  ({
    id: 1,
    sessionId: 's-1',
    agent: 'claude',
    startedAt: ago(30),
    endedAt: ago(20),
    title: 'Fixed the thing',
    summary: 'It was the clock.',
    described: true,
    ...over,
  }) as WorkLogEntry;

const released = (sessionId: unknown, runner: unknown, kind: IssueEvent['kind'] = 'claim_released'): IssueEvent => ({
  id: 1,
  actor: 'a-runner',
  kind,
  payload: { from: 'x', to: null, sessionId, runner },
  at: ago(19),
});

describe('gooseStateWords', () => {
  it('says nothing until the goose has been asked after', () => {
    expect(gooseStateWords(null, NOW)).toBeNull();
    expect(gooseStateWords(undefined, NOW)).toBeNull();
  });

  it('says a running goose is running and when it last swept', () => {
    expect(gooseStateWords(goose(), NOW)).toBe('The goose is running. It last swept 2 minutes ago.');
  });

  it('says a sweep inside the minute was a moment ago', () => {
    expect(gooseStateWords(goose({ lastSweepAt: ago(0) }), NOW)).toBe('The goose is running. It last swept a moment ago.');
  });

  it('says a running goose that has not swept has not', () => {
    expect(gooseStateWords(goose({ lastSweepAt: null }), NOW)).toBe('The goose is running. It has not swept yet.');
  });

  it('says a stopped goose in the attention panel’s own words', () => {
    const g = goose({ stopped: true, lastSweepAt: ago(30) });
    expect(gooseStateWords(g, NOW)).toBe(gooseStoppedWords(g, NOW));
    expect(gooseStateWords(goose({ stopped: true, lastSweepAt: null }), NOW)).toBe(
      'The goose has stopped. It has not run since the server started.',
    );
  });
});

describe('sweepWords', () => {
  it.each([
    [0, '0 ms'],
    [-5, '0 ms'],
    [Number.NaN, '0 ms'],
    [340, '340 ms'],
    [999, '999 ms'],
    [1000, '1.0 s'],
    [1240, '1.2 s'],
    [9949, '9.9 s'],
    [14_400, '14 s'],
    [59_400, '59 s'],
    [125_000, '2 min 5 s'],
  ])('reads %s as %s', (ms, words) => {
    expect(sweepWords(ms)).toBe(words);
  });
});

describe('runRowWords', () => {
  it('says when it first and last ran, its sweeps, its slowest, its checks and its open findings', () => {
    expect(runRowWords(run(), 2, NOW)).toEqual({
      when: 'first ran 3 hours ago, last ran 2 minutes ago',
      sweeps: '12 sweeps',
      slowest: 'slowest 1.2 s',
      checks: '7 checks',
      open: '2 findings open',
    });
  });

  it('says how many of the checks failed', () => {
    expect(runRowWords(run({ checksFailed: 2 }), 0, NOW).checks).toBe('7 checks, 2 failed');
  });

  it('reads a single sweep, check and finding in the singular', () => {
    const words = runRowWords(run({ sweeps: 1, checks: 1, firstRanAt: ago(2) }), 1, NOW);
    expect(words.sweeps).toBe('1 sweep');
    expect(words.checks).toBe('1 check');
    expect(words.open).toBe('1 finding open');
    expect(words.when).toBe('ran 2 minutes ago');
  });

  it('says none when none were open or none ran', () => {
    const words = runRowWords(run({ checks: 0 }), 0, NOW);
    expect(words.checks).toBe('no checks');
    expect(words.open).toBe('no findings open');
  });

  it('leaves the checks out when the server does not count them', () => {
    expect(runRowWords(run({ checks: undefined, checksFailed: undefined }), 0, NOW).checks).toBe('');
  });

  it('counts sweeps with its own helper', () => {
    expect(sweepCount(0)).toBe('0 sweeps');
    expect(sweepCount(1)).toBe('1 sweep');
  });
});

describe('findingsOpenAt', () => {
  const r = run({ lastRanAt: ago(10) });

  it('counts a finding opened before the last sweep and still open', () => {
    expect(findingsOpenAt([finding({ openedAt: ago(30) })], r)).toBe(1);
  });

  it('leaves out one opened after the last sweep', () => {
    expect(findingsOpenAt([finding({ openedAt: ago(5) })], r)).toBe(0);
  });

  it('leaves out one closed before the last sweep, and counts one closed after it', () => {
    expect(findingsOpenAt([finding({ openedAt: ago(60), closedAt: ago(20) })], r)).toBe(0);
    expect(findingsOpenAt([finding({ openedAt: ago(60), closedAt: ago(5) })], r)).toBe(1);
  });

  it('is none for no findings', () => {
    expect(findingsOpenAt([], r)).toBe(0);
  });
});

describe('findingLasted and findingTimeWords', () => {
  it('says nothing of how long an open finding lasted, and how long since it began', () => {
    const f = finding();
    expect(findingLasted(f)).toBeNull();
    expect(findingTimeWords(f, NOW)).toBe('began 2 hours ago');
  });

  it('says how long a closed finding lasted and when it closed', () => {
    const f = finding({ beganAt: ago(300), closedAt: ago(60) });
    expect(findingLasted(f)).toBe('4 hours');
    expect(findingTimeWords(f, NOW)).toBe('lasted 4 hours, closed 1 hour ago');
  });

  it('reaches days, and says under a minute below the first minute', () => {
    expect(findingLasted(finding({ beganAt: ago(3 * 24 * 60 + 5), closedAt: ago(5) }))).toBe('3 days');
    expect(findingLasted(finding({ beganAt: ago(10), closedAt: new Date(NOW.getTime() - 9 * 60_000 - 30_000).toISOString() }))).toBe('under a minute');
  });

  it('says only when it closed if the dates cannot be read', () => {
    const f = finding({ beganAt: 'nonsense', closedAt: ago(1) });
    expect(findingLasted(f)).toBeNull();
    expect(findingTimeWords(f, NOW)).toBe('closed 1 minute ago');
  });
});

describe('evidenceLines', () => {
  it('keeps the order the check gave, one label and value to a line', () => {
    const f = finding({
      evidence: [
        { label: 'Waiting', value: '3 hours' },
        { label: 'Head of train', value: 'AER-9' },
      ],
    });
    expect(evidenceLines(f)).toEqual([
      { label: 'Waiting', value: '3 hours' },
      { label: 'Head of train', value: 'AER-9' },
    ]);
  });

  it('drops a line with neither, and is empty for no evidence', () => {
    expect(evidenceLines(finding({ evidence: [{ label: ' ', value: '' }, { label: 'a', value: '' }] }))).toEqual([
      { label: 'a', value: '' },
    ]);
    expect(evidenceLines(finding())).toEqual([]);
  });
});

describe('ordering', () => {
  it('lists open findings in the server’s order and drops closed ones', () => {
    const a = finding({ id: 1 });
    const b = finding({ id: 2, closedAt: ago(1) });
    const c = finding({ id: 3 });
    expect(openFindings([a, b, c]).map((f) => f.id)).toEqual([1, 3]);
  });

  it('lists closed findings newest closed first, and does not move the list it was given', () => {
    const all = [
      finding({ id: 1, closedAt: ago(50) }),
      finding({ id: 2 }),
      finding({ id: 3, closedAt: ago(5) }),
      finding({ id: 4, closedAt: ago(5) }),
    ];
    expect(closedFindings(all).map((f) => f.id)).toEqual([4, 3, 1]);
    expect(all.map((f) => f.id)).toEqual([1, 2, 3, 4]);
  });

  it('lists runs newest first by their last sweep', () => {
    const runs = [run({ id: 1, lastRanAt: ago(30) }), run({ id: 2, lastRanAt: ago(1) }), run({ id: 3, lastRanAt: ago(10) })];
    expect(runsNewestFirst(runs).map((r) => r.id)).toEqual([2, 3, 1]);
    expect(runsNewestFirst([])).toEqual([]);
  });
});

describe('bugColumnName', () => {
  const statuses = [
    { id: 2, name: 'To Do' },
    { id: 3, name: 'In Progress' },
  ] as Status[];

  it('names the column the bug stands in', () => {
    expect(bugColumnName(statuses, { statusId: 3 })).toBe('In Progress');
  });

  it('is null for a column not among those read', () => {
    expect(bugColumnName(statuses, { statusId: 99 })).toBeNull();
    expect(bugColumnName([], { statusId: 3 })).toBeNull();
  });
});

describe('sessionReports', () => {
  it('lists the sessions newest first with title, summary, when and the runner that released it', () => {
    const entries = [
      entry({ id: 1, sessionId: 's-1', endedAt: ago(60), title: 'Older' }),
      entry({ id: 2, sessionId: 's-2', endedAt: ago(10), title: 'Newer' }),
    ];
    const events = [released('s-1', 'here:/repo'), released('s-2', 'there:/repo')];

    expect(sessionReports(entries, events)).toEqual([
      { id: 2, title: 'Newer', summary: 'It was the clock.', at: entries[1].endedAt, runner: 'there:/repo' },
      { id: 1, title: 'Older', summary: 'It was the clock.', at: entries[0].endedAt, runner: 'here:/repo' },
    ]);
  });

  it('shows no runner for a session no release names', () => {
    const [report] = sessionReports([entry()], [released('another', 'here:/repo')]);
    expect(report.runner).toBeNull();
  });

  it('ignores events of other kinds and releases that name no runner or no session', () => {
    const events = [
      released('s-1', 'here:/repo', 'claim_taken'),
      released('s-1', undefined),
      released('s-1', ''),
      released(undefined, 'here:/repo'),
      { ...released('s-1', 'x'), payload: null },
    ];
    expect(sessionReports([entry()], events)[0].runner).toBeNull();
  });

  it('says a session that never said what it did did not', () => {
    const [report] = sessionReports([entry({ described: false, title: null, summary: null })], []);
    expect(report.title).toBe(UNDESCRIBED_TITLE);
    expect(report.summary).toBe('');
  });

  it('is empty for no sessions', () => {
    expect(sessionReports([], [released('s-1', 'r')])).toEqual([]);
  });
});

describe('the empty states and the scope line', () => {
  it('says something for each empty list', () => {
    expect(noRunsWords()).toBe('The goose has not run yet.');
    expect(noOpenFindingsWords()).toMatch(/^Nothing is open\./);
    expect(noClosedFindingsWords()).toBe('Nothing has closed in the days kept.');
  });

  it('says runs cover the whole board', () => {
    expect(runsCoverWholeBoardWords()).toMatch(/whole board/);
  });

  it('shows runs to a caller who reaches everything, or whose server says nothing of scope', () => {
    expect(runsCoverWholeBoard(null)).toBe(true);
    expect(runsCoverWholeBoard({})).toBe(true);
    expect(runsCoverWholeBoard({ scope: { mode: 'all', projectIds: [] } })).toBe(true);
  });

  it('keeps runs from a caller with some projects or none', () => {
    expect(runsCoverWholeBoard({ scope: { mode: 'some', projectIds: [1] } })).toBe(false);
    expect(runsCoverWholeBoard({ scope: { mode: 'none', projectIds: [] } })).toBe(false);
  });
});
