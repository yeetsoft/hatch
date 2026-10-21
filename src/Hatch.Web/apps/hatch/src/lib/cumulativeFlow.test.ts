import { describe, expect, it } from 'vitest';
import {
  bandPolygon,
  bandStyle,
  bands,
  breakdown,
  isEmpty,
  momentLabel,
  peakLabel,
  pointIndexAt,
  slots,
  stackAt,
  tallestTotal,
} from './cumulativeFlow';
import { statusVars } from './color';
import type { CumulativeFlow, CumulativeFlowPoint, Status } from '../types';

const scale = { from: 0, to: 100 };

const point = (at: number, counts: number[]): CumulativeFlowPoint => ({
  at: new Date(at).toISOString(),
  counts,
});

const answer = (over: Partial<CumulativeFlow> = {}): CumulativeFlow => ({
  from: new Date(0).toISOString(),
  to: new Date(100).toISOString(),
  step: 'hour',
  columns: [1, 2, 3],
  points: [point(0, [3, 1, 2]), point(100, [0, 0, 0])],
  unresolved: 0,
  ...over,
});

const statuses: Status[] = [
  { id: 1, name: 'To Do', color: '#3366cc' },
  { id: 2, name: 'In Progress', color: '#cc9933' },
  { id: 3, name: 'Done', color: '#33cc66' },
] as Status[];

describe('stackAt', () => {
  it('stacks bottom-up, the last column at the baseline', () => {
    expect(stackAt([3, 1, 2])).toEqual([
      { lower: 0, upper: 2 },
      { lower: 2, upper: 3 },
      { lower: 3, upper: 6 },
    ]);
  });

  it('stacks nothing into an empty band at zero', () => {
    expect(stackAt([0, 0, 0])).toEqual([
      { lower: 0, upper: 0 },
      { lower: 0, upper: 0 },
      { lower: 0, upper: 0 },
    ]);
  });
});

describe('tallestTotal', () => {
  it('is the tallest grand total across every point', () => {
    expect(tallestTotal([point(0, [3, 1, 2]), point(1, [1, 1, 1])])).toBe(6);
  });

  it('is 0 for no points, guarding Math.max on empty', () => {
    expect(tallestTotal([])).toBe(0);
  });
});

describe('bands', () => {
  it('runs one series per column, bottom-up, scaled against the tallest total', () => {
    const series = bands(answer(), scale);

    expect(series).toHaveLength(3);
    series.forEach((s) => expect(s).toHaveLength(2));

    // Bottom band is the last column (count 2 of 6 at the first point).
    expect(series[0][0]).toEqual({ x: 0, lowerFraction: 0, upperFraction: 2 / 6 });
    // Top band is the first column, its upper bound the grand total.
    expect(series[2][0]).toEqual({ x: 0, lowerFraction: 3 / 6, upperFraction: 1 });
  });

  it('guards every fraction at 0, not NaN, when every count is zero', () => {
    const series = bands(answer({ points: [point(0, [0, 0, 0])] }), scale);

    for (const s of series) {
      expect(s[0].lowerFraction).toBe(0);
      expect(s[0].upperFraction).toBe(0);
    }
  });

  it('gives an empty series per column when there are no points', () => {
    const series = bands(answer({ points: [] }), scale);

    expect(series).toEqual([[], [], []]);
  });
});

describe('bandPolygon', () => {
  it('draws the upper edge left to right and the lower edge back right to left', () => {
    const band = [
      { x: 0, lowerFraction: 0, upperFraction: 0.5 },
      { x: 100, lowerFraction: 0, upperFraction: 0.5 },
    ];

    expect(bandPolygon(band, 100)).toBe('0,50 100,50 100,100 0,100');
  });
});

describe('peakLabel', () => {
  it('names the grand total at the tallest point', () => {
    expect(peakLabel([point(0, [14, 3, 23])])).toBe('peaking at 40');
  });
});

describe('slots', () => {
  it('is empty with no points', () => {
    expect(slots([], scale)).toEqual([]);
  });

  it('gives the one point the whole pane up to its own x', () => {
    expect(slots([point(40, [1])], scale)).toEqual([{ x: 0, width: 40 }]);
  });

  it('splits two points at their midpoint, the last ending at its own x', () => {
    const result = slots([point(20, [1]), point(80, [1])], scale);

    expect(result).toEqual([
      { x: 0, width: 50 },
      { x: 50, width: 30 },
    ]);
  });

  it('gives every inner point both its neighbours, over three', () => {
    const result = slots([point(10, [1]), point(50, [1]), point(90, [1])], scale);

    expect(result).toEqual([
      { x: 0, width: 30 },
      { x: 30, width: 40 },
      { x: 70, width: 20 },
    ]);
  });
});

describe('pointIndexAt', () => {
  it('is null with no points', () => {
    expect(pointIndexAt([], scale, 50)).toBeNull();
  });

  it('gives the one point at both edges', () => {
    const points = [point(40, [1])];

    expect(pointIndexAt(points, scale, 0)).toBe(0);
    expect(pointIndexAt(points, scale, 100)).toBe(0);
  });

  it('gives the first point at the left edge and the last past its own slot', () => {
    const points = [point(10, [1]), point(50, [1]), point(90, [1])];

    expect(pointIndexAt(points, scale, 0)).toBe(0);
    expect(pointIndexAt(points, scale, 100)).toBe(2);
  });

  it('resolves an exact slot boundary to the right-hand point', () => {
    const points = [point(20, [1]), point(80, [1])];

    expect(pointIndexAt(points, scale, 50)).toBe(1);
  });
});

describe('breakdown', () => {
  it('lists every column in board order, with the total', () => {
    expect(breakdown(point(0, [3, 1, 2]), [1, 2, 3], statuses)).toEqual({
      rows: [
        { columnId: 1, name: 'To Do', count: 3 },
        { columnId: 2, name: 'In Progress', count: 1 },
        { columnId: 3, name: 'Done', count: 2 },
      ],
      total: 6,
    });
  });

  it('names a column no longer on the board Unknown', () => {
    expect(breakdown(point(0, [5, 2]), [1, 99], statuses)).toEqual({
      rows: [
        { columnId: 1, name: 'To Do', count: 5 },
        { columnId: 99, name: 'Unknown', count: 2 },
      ],
      total: 7,
    });
  });
});

describe('momentLabel', () => {
  // Noon UTC, as lib/spend.test.ts's own fixtures are: the day is the same one
  // regardless of which zone the test runs in, which the hour component is
  // not and is asserted loosely instead.
  it('names the hour alongside the day on an hour step', () => {
    expect(momentLabel('2026-10-12T12:00:00.000Z', 'hour', 'en-GB')).toMatch(/^12 Oct \d{2}:\d{2}$/);
  });

  it('drops the time on a day or week step', () => {
    expect(momentLabel('2026-10-12T12:00:00.000Z', 'day', 'en-GB')).toBe('12 Oct');
    expect(momentLabel('2026-10-12T12:00:00.000Z', 'week', 'en-GB')).toBe('12 Oct');
  });
});

describe('bandStyle', () => {
  it("is the column's own status colour", () => {
    expect(bandStyle(statuses, 1)).toEqual(statusVars('#3366cc'));
  });

  it('falls back to the default grey for a column not among the statuses', () => {
    expect(bandStyle(statuses, 404)).toEqual(statusVars(undefined));
  });
});

describe('isEmpty', () => {
  it('is true with no points', () => {
    expect(isEmpty(answer({ points: [] }))).toBe(true);
  });

  it('is true when every count everywhere is zero', () => {
    expect(isEmpty(answer({ points: [point(0, [0, 0, 0]), point(1, [0, 0, 0])] }))).toBe(true);
  });

  it('is false when any count anywhere is non-zero', () => {
    expect(isEmpty(answer())).toBe(false);
  });
});
