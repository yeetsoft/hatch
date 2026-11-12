import { describe, expect, it } from 'vitest';
import type { IssueCard, Project, Status, Timeline, TimelineEntry, TimelineSegment } from '../types';
import { DEFAULT_STATUS_COLOR } from './color';
import {
  CHART_UNITS,
  CREATOR,
  OTHER_KEY,
  PROJECT,
  brushBox,
  brushWindow,
  creatorChoices,
  foldRows,
  makeScale,
  narrowedTo,
  noProjectWords,
  reportProjects,
  resolveCreator,
  resolveProject,
  resolveProjects,
  segmentStyle,
  segmentTitle,
  tickUnit,
  ticks,
  todayX,
  toggled,
  unresolvedPhrase,
  withFilter,
  withProjects,
  xOf,
} from './timeline';
import {
  DEFAULT_DAYS,
  presetOf,
  resizeWindow,
  resolveWindow,
  sameWindow,
  shiftWindow,
  windowEndingToday,
  withWindow,
  type ReportWindow,
} from './reportWindow';

const NOW = new Date('2026-10-09T12:00:00Z');
const DAY_MS = 86_400_000;
const LOCALE = 'en-GB';

const params = (query: string) => new URLSearchParams(query);

const days = (n: number) => {
  const from = '2026-10-01T00:00:00.000Z';

  return makeScale(from, new Date(Date.parse(from) + n * DAY_MS).toISOString());
};

describe('xOf', () => {
  const scale = makeScale('2026-10-01T00:00:00.000Z', '2026-10-11T00:00:00.000Z');

  it('runs from 0 at from to the full width at to', () => {
    expect(xOf(scale, '2026-10-01T00:00:00.000Z')).toBe(0);
    expect(xOf(scale, '2026-10-11T00:00:00.000Z')).toBe(CHART_UNITS);
    expect(CHART_UNITS).toBe(100);
  });

  it('puts the midpoint at 50', () => {
    expect(xOf(scale, '2026-10-06T00:00:00.000Z')).toBe(50);
  });

  it('clamps before from and after to', () => {
    expect(xOf(scale, '2026-09-01T00:00:00.000Z')).toBe(0);
    expect(xOf(scale, '2026-12-01T00:00:00.000Z')).toBe(100);
  });

  it('takes epoch milliseconds as well as an ISO string', () => {
    expect(xOf(scale, scale.from + 2.5 * DAY_MS)).toBe(25);
  });
});

describe('brushWindow', () => {
  const MINUTE = 60_000;
  // 1000px over ten days: a pixel is 864 seconds, so 100px is exactly 1 day.
  const scale = days(10);
  const at = (day: number) => new Date(scale.from + day * DAY_MS).toISOString();

  it('maps the two ends onto the scale', () => {
    expect(brushWindow(scale, 100, 300, 1000)).toEqual({ from: at(1), to: at(3) });
  });

  it('gives the same window for a drag either way', () => {
    expect(brushWindow(scale, 300, 100, 1000)).toEqual(brushWindow(scale, 100, 300, 1000));
  });

  it('clamps a press left of the ruler and a release past it', () => {
    expect(brushWindow(scale, -50, 1200, 1000)).toEqual({ from: at(0), to: at(10) });
  });

  it('rounds each end to the nearest whole minute', () => {
    // 100.01px is 1 day and 8.64 seconds; 200.02px is 2 days and 17.28 seconds.
    expect(brushWindow(scale, 100.01, 200.02, 1000)).toEqual({ from: at(1), to: at(2) });
    // 100.4px is 1 day, 5 minutes 45.6 seconds: rounds up to 6 minutes.
    expect(brushWindow(scale, 100.4, 300, 1000)?.from).toBe(new Date(scale.from + DAY_MS + 6 * MINUTE).toISOString());
  });

  it('is a click under 8px on a long scale', () => {
    const quarter = days(90);

    expect(brushWindow(quarter, 100, 107.9, 1000)).toBeNull();
    expect(brushWindow(quarter, 100, 100, 1000)).toBeNull();
  });

  it('is a window at exactly 8px', () => {
    expect(brushWindow(scale, 100, 108, 1000)).not.toBeNull();
  });

  it('is a click under a minute on a one-hour scale, where a minute is under 8px', () => {
    const hour = makeScale('2026-10-01T00:00:00.000Z', '2026-10-01T01:00:00.000Z');

    // 1000px over 60 minutes: 16.67px a minute, so 8px is under a minute.
    expect(brushWindow(hour, 0, 8, 1000)).toBeNull();
    expect(brushWindow(hour, 0, 15, 1000)).toBeNull();
  });

  it('is a window at exactly a minute on a one-hour scale', () => {
    const short = makeScale('2026-10-01T00:00:00.000Z', '2026-10-01T01:04:00.000Z');

    // 1024px over 64 minutes: a pixel is 3.75 seconds, so 16px is exactly one
    // minute and 8px, half of one, is under it. Powers of two keep it exact.
    expect(brushWindow(short, 0, 16, 1024)).toEqual({
      from: '2026-10-01T00:00:00.000Z',
      to: '2026-10-01T00:01:00.000Z',
    });
    expect(brushWindow(short, 0, 15, 1024)).toBeNull();
  });

  it('is a click under a minute on a narrow ruler', () => {
    const hour = makeScale('2026-10-01T00:00:00.000Z', '2026-10-01T01:00:00.000Z');

    // Half a pixel of a hundred is 18 seconds: under a minute, so no window.
    expect(brushWindow(hour, 1, 1.5, 100)).toBeNull();
  });

  it('is null with no width to map onto', () => {
    expect(brushWindow(scale, 0, 100, 0)).toBeNull();
    expect(brushWindow(scale, 0, 100, -5)).toBeNull();
  });
});

describe('brushBox', () => {
  it('is the span between the two ends, as percent of the width', () => {
    expect(brushBox(100, 300, 1000)).toEqual({ left: 10, width: 20 });
  });

  it('orders the ends', () => {
    expect(brushBox(300, 100, 1000)).toEqual({ left: 10, width: 20 });
  });

  it('clamps to the ruler on both sides', () => {
    expect(brushBox(-50, 1200, 1000)).toEqual({ left: 0, width: 100 });
  });

  it('is nothing with no width', () => {
    expect(brushBox(0, 100, 0)).toEqual({ left: 0, width: 0 });
  });
});

describe('tickUnit', () => {
  it.each([
    [1, 'threeHours'],
    [2, 'threeHours'],
    [3, 'day'],
    [7, 'day'],
    [31, 'day'],
    [32, 'week'],
    [183, 'week'],
    [184, 'month'],
  ] as const)('is %i days -> %s', (n, unit) => {
    expect(tickUnit(days(n))).toBe(unit);
  });
});

describe('tickUnit under two days', () => {
  const MIN = 60_000;
  const span = (ms: number) =>
    makeScale('2026-10-28T10:00:00.000Z', new Date(Date.parse('2026-10-28T10:00:00.000Z') + ms).toISOString());

  it.each([
    [59 * MIN, 'tenMinutes'],
    [60 * MIN, 'tenMinutes'],
    [61 * MIN, 'halfHour'],
    [3 * 60 * MIN, 'halfHour'],
    [6 * 60 * MIN, 'halfHour'],
    [6 * 60 * MIN + MIN, 'hour'],
    [12 * 60 * MIN, 'hour'],
    [12 * 60 * MIN + MIN, 'threeHours'],
    [2 * DAY_MS, 'threeHours'],
    [2 * DAY_MS + 1, 'day'],
    [3 * DAY_MS, 'day'],
  ] as const)('is %i ms -> %s', (ms, unit) => {
    expect(tickUnit(span(ms))).toBe(unit);
  });
});

describe('ticks', () => {
  const at = (iso: string) => Date.parse(iso);

  describe('by three hours', () => {
    const scale = makeScale('2026-10-28T00:00:00.000Z', '2026-10-29T00:00:00.000Z');

    it('has one tick per three hours, ends included, with the date on the first and on every midnight', () => {
      const result = ticks(scale, 0, LOCALE);

      expect(result.map((t) => t.label)).toEqual([
        '28 Oct 00:00',
        '03:00',
        '06:00',
        '09:00',
        '12:00',
        '15:00',
        '18:00',
        '21:00',
        '29 Oct 00:00',
      ]);
      expect(result.map((t) => t.x)).toEqual(Array.from({ length: 9 }, (_, i) => i * 12.5));
      expect(result.every((t) => t.unit === 'threeHours')).toBe(true);
    });

    it("lands on the reader's three-hour marks at offset 600", () => {
      const result = ticks(scale, 600, LOCALE);

      // The first mark at or after 10:00 local on the 28th is 12:00, 2 h in.
      expect(result[0]).toEqual({ x: (2 / 24) * 100, label: '28 Oct 12:00', unit: 'threeHours' });
      expect(result.map((t) => t.label)).toEqual([
        '28 Oct 12:00',
        '15:00',
        '18:00',
        '21:00',
        '29 Oct 00:00',
        '03:00',
        '06:00',
        '09:00',
      ]);
      expect(result[1].x - result[0].x).toBeCloseTo(12.5, 10);
    });
  });

  describe('by ten minutes', () => {
    const scale = makeScale('2026-10-28T10:00:00.000Z', '2026-10-28T11:00:00.000Z');

    it('has seven ticks ten minutes apart, ends included, with the date on the first', () => {
      const result = ticks(scale, 0, LOCALE);

      expect(result.map((t) => t.label)).toEqual([
        '28 Oct 10:00',
        '10:10',
        '10:20',
        '10:30',
        '10:40',
        '10:50',
        '11:00',
      ]);
      result.forEach((t, i) => expect(t.x).toBeCloseTo((i * 100) / 6, 10));
      expect(result.every((t) => t.unit === 'tenMinutes')).toBe(true);
    });

    it("lands on the reader's marks at +05:30", () => {
      const result = ticks(scale, 330, LOCALE);

      expect(result).toHaveLength(7);
      expect(result[0].label).toBe('28 Oct 15:30');
      expect(result[0].x).toBe(0);
      expect(result[6].label).toBe('16:30');
      expect(result[6].x).toBe(100);
    });
  });

  describe('by half hour', () => {
    const scale = makeScale('2026-10-28T21:00:00.000Z', '2026-10-29T03:00:00.000Z');

    it('has thirteen ticks, with the date on the first and at the midnight only', () => {
      const result = ticks(scale, 0, LOCALE);

      expect(result.map((t) => t.label)).toEqual([
        '28 Oct 21:00',
        '21:30',
        '22:00',
        '22:30',
        '23:00',
        '23:30',
        '29 Oct 00:00',
        '00:30',
        '01:00',
        '01:30',
        '02:00',
        '02:30',
        '03:00',
      ]);
      expect(result.every((t) => t.unit === 'halfHour')).toBe(true);
    });

    it("lands on the reader's marks at offset 600", () => {
      const result = ticks(scale, 600, LOCALE);

      // 21:00Z is 07:00 the next morning at +10:00.
      expect(result[0].label).toBe('29 Oct 07:00');
      expect(result[0].x).toBe(0);
    });
  });

  describe('by hour', () => {
    const scale = makeScale('2026-10-28T22:00:00.000Z', '2026-10-29T10:00:00.000Z');

    it('has thirteen ticks, with the date on the first and on the midnight', () => {
      const result = ticks(scale, 0, LOCALE);

      expect(result).toHaveLength(13);
      expect(result[0].label).toBe('28 Oct 22:00');
      expect(result[2].label).toBe('29 Oct 00:00');
      expect(result.filter((t) => /Oct/.test(t.label))).toHaveLength(2);
      expect(result.every((t) => t.unit === 'hour')).toBe(true);
    });
  });

  describe('by day', () => {
    const scale = makeScale('2026-10-28T00:00:00.000Z', '2026-11-07T00:00:00.000Z');

    it('has one tick per midnight, ends included, with the month on the first and on the 1st', () => {
      const result = ticks(scale, 0, LOCALE);

      expect(result.map((t) => t.label)).toEqual([
        '28 Oct',
        '29',
        '30',
        '31',
        '1 Nov',
        '2',
        '3',
        '4',
        '5',
        '6',
        '7',
      ]);
      expect(result.map((t) => t.x)).toEqual(Array.from({ length: 11 }, (_, i) => i * 10));
      expect(result.every((t) => t.unit === 'day')).toBe(true);
    });

    it("lands on the reader's midnights at offset 600", () => {
      const result = ticks(scale, 600, LOCALE);

      // The first local midnight after 10:00 local on the 28th is the 29th, 14 h in.
      expect(result[0]).toEqual({ x: (14 / 240) * 100, label: '29 Oct', unit: 'day' });
      expect(result.map((t) => t.label)).toEqual([
        '29 Oct',
        '30',
        '31',
        '1 Nov',
        '2',
        '3',
        '4',
        '5',
        '6',
        '7',
      ]);
      expect(result[1].x - result[0].x).toBeCloseTo(10, 10);
    });
  });

  describe('by week', () => {
    it('has a tick on each Monday, labelled with its date', () => {
      const scale = makeScale('2026-10-01T00:00:00.000Z', '2026-12-24T00:00:00.000Z');
      const result = ticks(scale, 0, LOCALE);

      expect(result.map((t) => t.label)).toEqual([
        '5 Oct',
        '12 Oct',
        '19 Oct',
        '26 Oct',
        '2 Nov',
        '9 Nov',
        '16 Nov',
        '23 Nov',
        '30 Nov',
        '7 Dec',
        '14 Dec',
        '21 Dec',
      ]);
      expect(result[0].x).toBeCloseTo((4 / 84) * 100, 10);
      expect(result.every((t) => t.unit === 'week')).toBe(true);
    });

    it('includes a boundary exactly at from and at to, in the reader\'s offset', () => {
      // Local Monday 5 Oct 00:00 and local Monday 28 Dec 00:00, at +10:00.
      const scale = makeScale('2026-10-04T14:00:00.000Z', '2026-12-27T14:00:00.000Z');
      const result = ticks(scale, 600, LOCALE);

      expect(result).toHaveLength(13);
      expect(result[0]).toEqual({ x: 0, label: '5 Oct', unit: 'week' });
      expect(result[12]).toEqual({ x: 100, label: '28 Dec', unit: 'week' });
    });
  });

  describe('by month', () => {
    const scale = makeScale('2026-11-15T00:00:00.000Z', '2028-01-15T00:00:00.000Z');

    it('has a tick on each 1st, with the year on the first and on every January', () => {
      const result = ticks(scale, 0, LOCALE);

      expect(result).toHaveLength(14);
      expect(result[0].label).toBe('Dec 2026');
      expect(result[1].label).toBe('Jan 2027');
      expect(result[2].label).toBe('Feb');
      expect(result[13].label).toBe('Jan 2028');
      // Every other month is bare letters, whichever way the locale spells September.
      expect(result.slice(2, 13).every((t) => /^\D+$/.test(t.label))).toBe(true);
      expect(result.every((t) => t.unit === 'month')).toBe(true);
    });

    it('places each tick at its 1st', () => {
      const result = ticks(scale, 0, LOCALE);

      expect(result[0].x).toBeCloseTo(((at('2026-12-01T00:00:00Z') - scale.from) / (scale.to - scale.from)) * 100, 10);
      expect(result[1].x).toBeCloseTo(((at('2027-01-01T00:00:00Z') - scale.from) / (scale.to - scale.from)) * 100, 10);
    });

    it("lands on the reader's 1st at offset 600", () => {
      const wide = makeScale('2026-11-15T00:00:00.000Z', '2027-06-01T00:00:00.000Z');
      const result = ticks(wide, 600, LOCALE);

      // 1 Dec 00:00 at +10:00 is 30 Nov 14:00 UTC.
      expect(result[0].label).toBe('Dec 2026');
      expect(result[0].x).toBeCloseTo(((at('2026-11-30T14:00:00Z') - wide.from) / (wide.to - wide.from)) * 100, 10);
      expect(result.map((t) => t.label).slice(0, 3)).toEqual(['Dec 2026', 'Jan 2027', 'Feb']);
    });
  });

  it('returns nothing for an empty window', () => {
    expect(ticks({ from: 5, to: 5 }, 0, LOCALE)).toEqual([]);
  });
});

describe('todayX', () => {
  const scale = makeScale('2026-10-01T00:00:00.000Z', '2026-10-11T00:00:00.000Z');

  it('is where now sits inside the window', () => {
    expect(todayX(scale, new Date('2026-10-06T00:00:00Z'))).toBe(50);
  });

  it('is 0 at from', () => {
    expect(todayX(scale, new Date('2026-10-01T00:00:00Z'))).toBe(0);
  });

  it('is null at to, and outside the window', () => {
    expect(todayX(scale, new Date('2026-10-11T00:00:00Z'))).toBeNull();
    expect(todayX(scale, new Date('2026-09-30T23:59:59Z'))).toBeNull();
    expect(todayX(scale, new Date('2026-12-01T00:00:00Z'))).toBeNull();
  });
});

const entry = (
  key: string,
  children: TimelineEntry[] = [],
  extra: Partial<TimelineEntry> = {},
): TimelineEntry => ({
  issue: { key, title: `title ${key}` } as IssueCard,
  createdBy: 'someone',
  isLeaf: children.length === 0,
  segments: [],
  children,
  ...extra,
});

const timeline = (epics: TimelineEntry[], other: TimelineEntry | null = null): Timeline => ({
  from: '2026-07-12T00:00:00.000Z',
  to: '2026-10-10T00:00:00.000Z',
  epics,
  other,
  creators: [],
  unresolved: 0,
});

const keys = (rows: { entry: TimelineEntry }[]) => rows.map((row) => row.entry.issue.key);

const segment = (statusId: number, from: string, to: string | null): TimelineSegment => ({ statusId, from, to });

describe('foldRows', () => {
  const tree = timeline(
    [entry('E-2', [entry('S-1', [entry('T-1')]), entry('S-2')]), entry('E-1')],
    entry(OTHER_KEY, [entry('L-1')]),
  );

  it('keeps the epics in the order answered and puts Other last', () => {
    expect(keys(foldRows(tree, new Set()))).toEqual(['E-2', 'E-1', OTHER_KEY]);
  });

  it('draws no Other row when other is null', () => {
    const rows = foldRows(timeline([entry('E-1')]), new Set());

    expect(keys(rows)).toEqual(['E-1']);
    expect(rows.some((row) => row.isOther)).toBe(false);
  });

  it('shows top-level rows only when everything is collapsed', () => {
    const rows = foldRows(tree, new Set());

    expect(rows.map((row) => row.depth)).toEqual([0, 0, 0]);
    expect(rows.map((row) => row.expanded)).toEqual([false, false, false]);
  });

  it('reveals an expanded epic\'s children at depth 1, and a grandchild only when its parent is open too', () => {
    const one = foldRows(tree, new Set(['E-2']));

    expect(keys(one)).toEqual(['E-2', 'S-1', 'S-2', 'E-1', OTHER_KEY]);
    expect(one.map((row) => row.depth)).toEqual([0, 1, 1, 0, 0]);

    const two = foldRows(tree, new Set(['E-2', 'S-1']));

    expect(keys(two)).toEqual(['E-2', 'S-1', 'T-1', 'S-2', 'E-1', OTHER_KEY]);
    expect(two.map((row) => row.depth)).toEqual([0, 1, 2, 1, 0, 0]);
  });

  it('does not reveal a grandchild when only the child is in the set', () => {
    expect(keys(foldRows(tree, new Set(['S-1'])))).toEqual(['E-2', 'E-1', OTHER_KEY]);
  });

  it('reveals the loose roots when other is expanded, and marks the row', () => {
    const rows = foldRows(tree, new Set([OTHER_KEY]));

    expect(keys(rows)).toEqual(['E-2', 'E-1', OTHER_KEY, 'L-1']);
    expect(rows.map((row) => row.isOther)).toEqual([false, false, true, false]);
    expect(rows[3].depth).toBe(1);
  });

  it('is expandable by children present, not by isLeaf', () => {
    const quiet = entry('S-9', [], { isLeaf: false });
    const rows = foldRows(timeline([entry('E-1', [quiet])]), new Set(['E-1', 'S-9']));

    expect(rows[1].expandable).toBe(false);
    expect(rows[1].expanded).toBe(false);
    expect(rows[0].expandable).toBe(true);
    expect(rows[0].expanded).toBe(true);
  });

  it('carries each entry untouched, so a parent keeps only its own segments', () => {
    const own = [segment(1, '2026-10-01T12:00:00Z', '2026-10-02T12:00:00Z')];
    const child = entry('S-1', [], { segments: [segment(2, '2026-10-03T12:00:00Z', null)] });
    const epic = entry('E-1', [child], { segments: own });
    const rows = foldRows(timeline([epic]), new Set(['E-1']));

    expect(rows[0].entry).toBe(epic);
    expect(rows[0].entry.segments).toEqual(own);
    expect(rows[1].entry).toBe(child);
  });

  it('leaves a hidden epic out', () => {
    expect(keys(foldRows(tree, new Set(), new Set(['E-2'])))).toEqual(['E-1', OTHER_KEY]);
  });

  it('leaves a hidden Other out', () => {
    expect(keys(foldRows(tree, new Set(), new Set([OTHER_KEY])))).toEqual(['E-2', 'E-1']);
  });

  it('leaves a hidden epic\'s expanded children out with it', () => {
    const open = new Set(['E-2', 'S-1', OTHER_KEY]);

    expect(keys(foldRows(tree, open, new Set(['E-2'])))).toEqual(['E-1', OTHER_KEY, 'L-1']);
  });

  it('changes nothing for a hidden key that names no top-level entry', () => {
    const open = new Set(['E-2']);

    expect(foldRows(tree, open, new Set(['nope', 'S-2']))).toEqual(foldRows(tree, open));
  });

  it('never touches the answer it folds', () => {
    const before = structuredClone(tree);

    foldRows(tree, new Set(['E-2', OTHER_KEY]), new Set(['E-2', OTHER_KEY]));

    expect(tree).toEqual(before);
  });

  it('hides nothing by default', () => {
    expect(foldRows(tree, new Set(['E-2']))).toEqual(foldRows(tree, new Set(['E-2']), new Set()));
  });
});

describe('toggled', () => {
  it('adds a key that is absent', () => {
    expect([...toggled(new Set(['a']), 'b')].sort()).toEqual(['a', 'b']);
  });

  it('removes a key that is present', () => {
    expect([...toggled(new Set(['a', 'b']), 'a')]).toEqual(['b']);
  });

  it('leaves the input unchanged and returns a new set', () => {
    const before = new Set(['a']);
    const added = toggled(before, 'b');
    const removed = toggled(before, 'a');

    expect([...before]).toEqual(['a']);
    expect(added).not.toBe(before);
    expect(removed).not.toBe(before);
  });
});

describe('segmentTitle', () => {
  it('names a closed segment from and to', () => {
    const closed = segment(1, '2026-10-03T12:00:00Z', '2026-10-09T12:00:00Z');

    expect(segmentTitle('In Progress', closed, LOCALE)).toBe('In Progress, 3 Oct to 9 Oct');
  });

  it('says onward while the segment is open', () => {
    const open = segment(1, '2026-10-03T12:00:00Z', null);

    expect(segmentTitle('In Progress', open, LOCALE)).toBe('In Progress, 3 Oct onward');
  });
});

describe('segmentStyle', () => {
  const statuses = [{ id: 4, name: 'In Progress', color: '#2563eb' }] as Status[];
  const at = (statusId: number) => segment(statusId, '2026-10-03T12:00:00Z', null);

  it("gives a known status's colour and ink", () => {
    const style = segmentStyle(statuses, at(4), false);

    expect(style?.['--status-color']).toBe('#2563eb');
    expect(style?.['--status-ink']).toBeTruthy();
  });

  it('falls back to the default grey for an unknown id', () => {
    expect(segmentStyle(statuses, at(99), false)?.['--status-color']).toBe(DEFAULT_STATUS_COLOR);
  });

  it('is null on Other, whatever the status', () => {
    expect(segmentStyle(statuses, at(4), true)).toBeNull();
  });
});

describe('unresolvedPhrase', () => {
  it('is null at zero', () => {
    expect(unresolvedPhrase(0)).toBeNull();
  });

  it('is singular at one', () => {
    expect(unresolvedPhrase(1)).toBe('1 column change could not be placed, because the column has since been renamed.');
  });

  it('is plural above one', () => {
    expect(unresolvedPhrase(2)).toBe('2 column changes could not be placed, because the column has since been renamed.');
  });
});

/** A window `count` days long ending at `to`. */
const ending = (to: string, count: number) => ({
  from: new Date(Date.parse(to) - count * DAY_MS).toISOString(),
  to,
});

const D = '2026-10-10T00:00:00.000Z';

describe('resolveProject', () => {
  const projects = [
    { id: 1, key: 'HA' },
    { id: 2, key: 'AER' },
  ] as Project[];

  it('finds the project by key', () => {
    expect(resolveProject(params('project=AER'), projects)).toBe(projects[1]);
  });

  it('trims the key', () => {
    expect(resolveProject(params('project=%20HA%20'), projects)).toBe(projects[0]);
  });

  it('is null when absent or empty', () => {
    expect(resolveProject(params(''), projects)).toBeNull();
    expect(resolveProject(params('project='), projects)).toBeNull();
    expect(resolveProject(params('project=%20'), projects)).toBeNull();
  });

  it('is null for a key naming no project', () => {
    expect(resolveProject(params('project=ZZ'), projects)).toBeNull();
  });

  it('never reads an id as a key', () => {
    expect(resolveProject(params('project=1'), projects)).toBeNull();
  });
});

describe('resolveProjects', () => {
  const projects = [
    { id: 1, key: 'HA' },
    { id: 2, key: 'AER' },
    { id: 3, key: 'PYC' },
  ] as Project[];

  it('finds the projects named by repeated keys, in projects order', () => {
    expect(resolveProjects(params('project=PYC&project=HA'), projects)).toEqual([projects[0], projects[2]]);
  });

  it('drops an unknown key silently, keeping a known one alongside it', () => {
    expect(resolveProjects(params('project=HA&project=ZZ'), projects)).toEqual([projects[0]]);
  });

  it('is every project when every key given is unknown', () => {
    expect(resolveProjects(params('project=ZZ&project=YY'), projects)).toEqual(projects);
  });

  it('is every project when there is no project param at all', () => {
    expect(resolveProjects(params(''), projects)).toEqual(projects);
  });

  it('dedupes a repeated key', () => {
    expect(resolveProjects(params('project=HA&project=HA'), projects)).toEqual([projects[0]]);
  });

  it('is no project when the param is present and empty', () => {
    expect(resolveProjects(params('project='), projects)).toEqual([]);
    expect(resolveProjects(params('project=&project='), projects)).toEqual([]);
    expect(resolveProjects(params('project=%20'), projects)).toEqual([]);
  });

  it('reads an empty value beside a key as that key', () => {
    expect(resolveProjects(params('project=&project=HA'), projects)).toEqual([projects[0]]);
  });

  it('keeps an unknown key and no param as every project', () => {
    expect(resolveProjects(params('project=ZZ'), projects)).toEqual(projects);
    expect(resolveProjects(params(''), projects)).toEqual(projects);
  });
});

describe('withProjects', () => {
  const allKeys = ['HA', 'AER', 'PYC'];

  it('writes one project param per key, in allKeys order', () => {
    expect(withProjects(params(''), ['PYC', 'HA'], allKeys).getAll('project')).toEqual(['HA', 'PYC']);
  });

  it('replaces whatever project params were already there', () => {
    expect(withProjects(params('project=ZZ&project=HA'), ['AER'], allKeys).getAll('project')).toEqual(['AER']);
  });

  it('writes no project param at all when every key is chosen - the All round trip', () => {
    const written = withProjects(params('project=HA'), allKeys, allKeys);

    expect(written.has('project')).toBe(false);
    expect(resolveProjects(written, allKeys.map((key, i) => ({ id: i, key }) as Project))).toEqual(
      allKeys.map((key, i) => ({ id: i, key })),
    );
  });

  it('keeps every other param', () => {
    expect(withProjects(params('from=a&to=b'), ['HA'], allKeys).get('from')).toBe('a');
  });

  it('writes exactly one empty project param for no keys, keeping the rest', () => {
    const written = withProjects(params('from=a'), [], allKeys);

    expect(written.getAll('project')).toEqual(['']);
    expect(written.get('from')).toBe('a');
  });

  it('round-trips an empty choice through project=', () => {
    const projects = allKeys.map((key, i) => ({ id: i, key }) as Project);

    expect(resolveProjects(withProjects(params('project=HA'), [], allKeys), projects)).toEqual([]);
  });

  it('does not mutate its input', () => {
    const input = params('project=HA');
    withProjects(input, ['AER'], allKeys);

    expect(input.toString()).toBe('project=HA');
  });
});

describe('noProjectWords', () => {
  it('is the sentence', () => {
    expect(noProjectWords()).toBe('No project is chosen.');
  });
});

describe('narrowedTo', () => {
  const projects = [
    { id: 1, key: 'HA' },
    { id: 2, key: 'AER' },
  ] as Project[];

  it('is the picked projects when they are fewer than all', () => {
    expect(narrowedTo([projects[1]], projects)).toEqual([projects[1]]);
  });

  it('is undefined when every project is picked', () => {
    expect(narrowedTo(projects, projects)).toBeUndefined();
  });

  it('is undefined on a single-project install', () => {
    expect(narrowedTo([projects[0]], [projects[0]])).toBeUndefined();
  });
});

describe('reportProjects', () => {
  const projects = [
    { id: 1, key: 'HA' },
    { id: 2, key: 'AER' },
    { id: 3, key: 'PYC' },
  ] as Project[];
  const inScope = [projects[0], projects[1]];

  it('offers, chooses and narrows to the scope when no project is named', () => {
    const got = reportProjects(params(''), projects, ['HA', 'AER']);

    expect(got.offered).toEqual(inScope);
    expect(got.chosen).toEqual(inScope);
    expect(got.narrowed).toEqual(inScope);
  });

  it('drops a project param outside the scope', () => {
    const got = reportProjects(params('project=PYC'), projects, ['HA', 'AER']);

    expect(got.chosen).toEqual(inScope);
    expect(got.narrowed).toEqual(inScope);
  });

  it('chooses a project inside the scope', () => {
    const got = reportProjects(params('project=AER'), projects, ['HA', 'AER']);

    expect(got.chosen).toEqual([projects[1]]);
    expect(got.narrowed).toEqual([projects[1]]);
  });

  it('does not narrow with no scope and no param', () => {
    const got = reportProjects(params(''), projects, []);

    expect(got.offered).toEqual(projects);
    expect(got.chosen).toEqual(projects);
    expect(got.narrowed).toBeUndefined();
  });

  it('writes the clean URL for All in scope', () => {
    const { offered } = reportProjects(params('project=HA'), projects, ['HA', 'AER']);

    expect(
      withProjects(
        params('project=HA'),
        ['HA', 'AER'],
        offered.map((p) => p.key),
      ).has('project'),
    ).toBe(false);
  });
});

describe('resolveCreator', () => {
  it('is empty, meaning anyone, when absent', () => {
    expect(resolveCreator(params(''))).toBe('');
  });

  it('reads a name with a space in it', () => {
    expect(resolveCreator(params('creator=Ada%20Lovelace'))).toBe('Ada Lovelace');
  });

  it('is empty for whitespace alone', () => {
    expect(resolveCreator(params('creator=%20%20'))).toBe('');
  });
});

describe('creatorChoices', () => {
  const creators = ['Zed', 'Ada', 'Mo'];

  it('is unchanged for no selection or one already present', () => {
    expect(creatorChoices(creators, '')).toEqual(creators);
    expect(creatorChoices(creators, 'Ada')).toEqual(creators);
  });

  it('appends an absent name last', () => {
    expect(creatorChoices(creators, 'Grace')).toEqual(['Zed', 'Ada', 'Mo', 'Grace']);
  });

  it('keeps the order given', () => {
    expect(creatorChoices(['b', 'a'], 'c')).toEqual(['b', 'a', 'c']);
  });

  it('does not mutate its input', () => {
    const input = ['Zed', 'Ada'];
    creatorChoices(input, 'Grace');

    expect(input).toEqual(['Zed', 'Ada']);
  });
});

describe('withFilter', () => {
  it('sets a filter', () => {
    expect(withFilter(params(''), PROJECT, 'HA').get('project')).toBe('HA');
  });

  it('replaces a filter', () => {
    expect(withFilter(params('creator=Ada'), CREATOR, 'Mo').getAll('creator')).toEqual(['Mo']);
  });

  it('deletes a filter for an empty value', () => {
    expect(withFilter(params('project=HA'), PROJECT, '').has('project')).toBe(false);
  });

  it('keeps the window and the other filter', () => {
    const next = withFilter(params('from=a&to=b&creator=Ada'), PROJECT, 'HA');

    expect(next.get('from')).toBe('a');
    expect(next.get('to')).toBe('b');
    expect(next.get('creator')).toBe('Ada');
  });

  it('does not mutate its input', () => {
    const input = params('project=HA');
    withFilter(input, PROJECT, '');
    withFilter(input, CREATOR, 'Ada');

    expect(input.toString()).toBe('project=HA');
  });
});

describe('as the page composes it', () => {
  /** What `move` chains: resolve the URL, step, write, and read the written URL back. */
  const roundTrip = (startQuery: string, step: (w: ReportWindow) => ReportWindow) => {
    const start = params(startQuery);
    const next = step(resolveWindow(start, NOW, 0));
    const written = withWindow(start, next);

    return { next, written, read: resolveWindow(params(written.toString()), NOW, 0) };
  };

  it.each([
    ['from the default', ''],
    ['from range=1y', 'range=1y'],
  ])('Earlier %s reads back as the window it wrote', (_, query) => {
    const { next, written, read } = roundTrip(query, (w) => shiftWindow(w, -1));

    expect(read).toEqual(next);
    expect(written.has('from') && written.has('to')).toBe(true);
    expect(written.has('range')).toBe(false);
  });

  it.each([
    ['from the default', ''],
    ['from range=1y', 'range=1y'],
  ])('a resize to 6m %s reads back as the window it wrote', (_, query) => {
    const { next, written, read } = roundTrip(query, (w) => resizeWindow(w, '6m', NOW));

    expect(read).toEqual(next);
    expect(presetOf(read)).toBe('6m');
    expect(written.has('range')).toBe(false);
  });

  it('Today on the default window is the window already showing, so nothing is written', () => {
    const shown = resolveWindow(params(''), NOW, 0);

    expect(sameWindow(windowEndingToday(shown, NOW, 0), shown)).toBe(true);
  });

  it('writing the window after the filters keeps both filters', () => {
    const filtered = withFilter(withFilter(params('range=1y'), PROJECT, 'HA'), CREATOR, 'Ada Lovelace');
    const written = withWindow(filtered, ending(D, 30));

    expect(written.get('project')).toBe('HA');
    expect(written.get('creator')).toBe('Ada Lovelace');
    expect(written.has('range')).toBe(false);
  });

  it('reads range=1y alone as the year ending at the next midnight', () => {
    expect(resolveWindow(params('range=1y'), NOW, 0)).toEqual(ending(D, 365));
  });

  it('reads from and to as exactly themselves, over range', () => {
    const w = ending('2026-08-01T00:00:00.000Z', 30);

    expect(resolveWindow(params(`range=1y&from=${w.from}&to=${w.to}`), NOW, 0)).toEqual(w);
  });

  it('reads neither as the default', () => {
    expect(resolveWindow(params(''), NOW, 0)).toEqual(ending(D, DEFAULT_DAYS));
  });
});
