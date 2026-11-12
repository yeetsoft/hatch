import { describe, expect, it } from 'vitest';
import {
  bandPolygon,
  bandStyle,
  bands,
  breakdown,
  cutKeys,
  isEmpty,
  labelFor,
  momentLabel,
  peakLabel,
  pinAfter,
  pointIndexAt,
  projectsSummary,
  resolveColourBy,
  slots,
  stackAt,
  tallestTotal,
  withColourBy,
} from './cumulativeFlow';
import { statusVars } from './color';
import { markVars } from '@hatch/ui';
import type { CumulativeFlow, CumulativeFlowPoint, Project, Status } from '../types';

const scale = { from: 0, to: 100 };

const point = (at: number, counts: number[], projectCounts: number[] = []): CumulativeFlowPoint => ({
  at: new Date(at).toISOString(),
  counts,
  projectCounts,
});

const answer = (over: Partial<CumulativeFlow> = {}): CumulativeFlow => ({
  from: new Date(0).toISOString(),
  to: new Date(100).toISOString(),
  stepMinutes: 60,
  columns: [1, 2, 3],
  projects: [],
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
    expect(tallestTotal([point(0, [3, 1, 2]), point(1, [1, 1, 1])], 'column')).toBe(6);
  });

  it('is 0 for no points, guarding Math.max on empty', () => {
    expect(tallestTotal([], 'column')).toBe(0);
  });

  it('is the same total cut by project, by construction', () => {
    const points = [point(0, [3, 1, 2], [4, 2]), point(1, [1, 1, 1], [2, 1])];
    expect(tallestTotal(points, 'project')).toBe(tallestTotal(points, 'column'));
  });
});

describe('bands', () => {
  it('runs one series per column, bottom-up, scaled against the tallest total', () => {
    const series = bands(answer(), scale, 'column');

    expect(series).toHaveLength(3);
    series.forEach((s) => expect(s).toHaveLength(2));

    // Bottom band is the last column (count 2 of 6 at the first point).
    expect(series[0][0]).toEqual({ x: 0, lowerFraction: 0, upperFraction: 2 / 6 });
    // Top band is the first column, its upper bound the grand total.
    expect(series[2][0]).toEqual({ x: 0, lowerFraction: 3 / 6, upperFraction: 1 });
  });

  it('guards every fraction at 0, not NaN, when every count is zero', () => {
    const series = bands(answer({ points: [point(0, [0, 0, 0])] }), scale, 'column');

    for (const s of series) {
      expect(s[0].lowerFraction).toBe(0);
      expect(s[0].upperFraction).toBe(0);
    }
  });

  it('gives an empty series per column when there are no points', () => {
    const series = bands(answer({ points: [] }), scale, 'column');

    expect(series).toEqual([[], [], []]);
  });

  it('runs one series per project when cut by project', () => {
    const series = bands(
      answer({ projects: ['HA', 'AER'], points: [point(0, [3, 1, 2], [4, 2])] }),
      scale,
      'project',
    );

    expect(series).toHaveLength(2);
    expect(series[0][0]).toEqual({ x: 0, lowerFraction: 0, upperFraction: 2 / 6 });
    expect(series[1][0]).toEqual({ x: 0, lowerFraction: 2 / 6, upperFraction: 1 });
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
    expect(peakLabel([point(0, [14, 3, 23])], 'column')).toBe('peaking at 40');
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
    expect(breakdown(point(0, [3, 1, 2]), [1, 2, 3], 'column', statuses)).toEqual({
      rows: [
        { key: 1, name: 'To Do', count: 3 },
        { key: 2, name: 'In Progress', count: 1 },
        { key: 3, name: 'Done', count: 2 },
      ],
      total: 6,
    });
  });

  it('names a column no longer on the board Unknown', () => {
    expect(breakdown(point(0, [5, 2]), [1, 99], 'column', statuses)).toEqual({
      rows: [
        { key: 1, name: 'To Do', count: 5 },
        { key: 99, name: 'Unknown', count: 2 },
      ],
      total: 7,
    });
  });

  it('lists every project in scope, cut by project', () => {
    expect(breakdown(point(0, [3, 1, 2], [4, 2]), ['HA', 'AER'], 'project', statuses)).toEqual({
      rows: [
        { key: 'HA', name: 'HA', count: 4 },
        { key: 'AER', name: 'AER', count: 2 },
      ],
      total: 6,
    });
  });
});

describe('momentLabel', () => {
  // Noon UTC, as lib/spend.test.ts's own fixtures are: the day is the same one
  // regardless of which zone the test runs in, which the hour component is
  // not and is asserted loosely instead.
  it.each([1, 15, 720])('names the time alongside the day on a %i minute step', (stepMinutes) => {
    expect(momentLabel('2026-10-12T12:00:00.000Z', stepMinutes, 'en-GB')).toMatch(/^12 Oct \d{2}:\d{2}$/);
  });

  it.each([1440, 10080])('drops the time on a %i minute step', (stepMinutes) => {
    expect(momentLabel('2026-10-12T12:00:00.000Z', stepMinutes, 'en-GB')).toBe('12 Oct');
  });
});

describe('bandStyle', () => {
  it("is the column's own status colour", () => {
    expect(bandStyle('column', 1, statuses, projects)).toEqual(statusVars('#3366cc'));
  });

  it('falls back to the default grey for a column not among the statuses', () => {
    expect(bandStyle('column', 404, statuses, projects)).toEqual(statusVars(undefined));
  });

  it("is the project's own colour, cut by project", () => {
    expect(bandStyle('project', 'HA', statuses, projects)).toEqual(markVars(projects[0].color));
  });

  it('falls back to the default grey for a project not in scope', () => {
    expect(bandStyle('project', 'ZZ', statuses, projects)).toEqual(markVars(undefined));
  });
});

describe('pinAfter', () => {
  it('pins the index a press names', () => {
    expect(pinAfter(null, 3, { type: 'press', index: 1 })).toBe(1);
  });

  it('repoints the pin a move names', () => {
    expect(pinAfter(1, 3, { type: 'move', index: 2 })).toBe(2);
  });

  it('clamps a press or a move past either end', () => {
    expect(pinAfter(null, 3, { type: 'press', index: 5 })).toBe(2);
    expect(pinAfter(null, 3, { type: 'move', index: -1 })).toBe(0);
  });

  it('leaves the pin alone when a press or a move names no index', () => {
    expect(pinAfter(1, 3, { type: 'press', index: null })).toBe(1);
    expect(pinAfter(1, 3, { type: 'move', index: null })).toBe(1);
    expect(pinAfter(null, 3, { type: 'move', index: null })).toBeNull();
  });

  it('steps from nothing pinned to the last point, whichever way delta points', () => {
    expect(pinAfter(null, 3, { type: 'step', delta: 1 })).toBe(2);
    expect(pinAfter(null, 3, { type: 'step', delta: -1 })).toBe(2);
  });

  it('steps the pin one point', () => {
    expect(pinAfter(1, 3, { type: 'step', delta: 1 })).toBe(2);
    expect(pinAfter(1, 3, { type: 'step', delta: -1 })).toBe(0);
  });

  it('clamps a step at both ends', () => {
    expect(pinAfter(2, 3, { type: 'step', delta: 1 })).toBe(2);
    expect(pinAfter(0, 3, { type: 'step', delta: -1 })).toBe(0);
  });

  it('clears the pin on a new answer', () => {
    expect(pinAfter(1, 3, { type: 'answer' })).toBeNull();
  });

  it('is null for every action once count is 0', () => {
    expect(pinAfter(0, 0, { type: 'press', index: 0 })).toBeNull();
    expect(pinAfter(0, 0, { type: 'move', index: 0 })).toBeNull();
    expect(pinAfter(0, 0, { type: 'step', delta: 1 })).toBeNull();
    expect(pinAfter(0, 0, { type: 'answer' })).toBeNull();
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

const projects = [
  { id: 1, key: 'HA', name: 'Hatch', color: '#112233' },
  { id: 2, key: 'AER', name: 'Aerie' },
  { id: 3, key: 'PYC', name: 'Pycnic' },
] as Project[];
const allKeys = projects.map((p) => p.key);

describe('projectsSummary', () => {
  it('is None when no project is chosen', () => {
    expect(projectsSummary([], projects)).toBe('None');
  });

  it('is All when every project is chosen', () => {
    expect(projectsSummary(allKeys, projects)).toBe('All');
  });

  it('names the one missing when all but one is chosen', () => {
    expect(projectsSummary(['HA', 'PYC'], projects)).toBe('All but AER');
  });

  it('joins the chosen keys when several, but not all but one, are chosen', () => {
    expect(projectsSummary(['PYC', 'HA'], [...projects, { id: 4, key: 'SL', name: 'Sail' } as Project])).toBe(
      'HA, PYC',
    );
  });

  it('names the one key when only one is chosen', () => {
    expect(projectsSummary(['AER'], projects)).toBe('AER');
  });
});

describe('resolveColourBy', () => {
  it('reads column when by is absent', () => {
    expect(resolveColourBy(new URLSearchParams())).toBe('column');
  });

  it('reads project when by=project', () => {
    expect(resolveColourBy(new URLSearchParams('by=project'))).toBe('project');
  });

  it('reads column for any other value of by', () => {
    expect(resolveColourBy(new URLSearchParams('by=status'))).toBe('column');
  });
});

describe('withColourBy', () => {
  it('sets by=project', () => {
    expect(withColourBy(new URLSearchParams(), 'project').get('by')).toBe('project');
  });

  it('clears by for column, the default', () => {
    expect(withColourBy(new URLSearchParams('by=project'), 'column').has('by')).toBe(false);
  });

  it('keeps every other param untouched', () => {
    const next = withColourBy(new URLSearchParams('projects=HA'), 'project');
    expect(next.get('projects')).toBe('HA');
    expect(next.get('by')).toBe('project');
  });

  it('never mutates its input', () => {
    const params = new URLSearchParams();
    withColourBy(params, 'project');
    expect(params.has('by')).toBe(false);
  });
});

describe('labelFor', () => {
  it("names a column's own status", () => {
    expect(labelFor('column', 1, statuses)).toBe('To Do');
  });

  it('names a column no longer on the board Unknown', () => {
    expect(labelFor('column', 404, statuses)).toBe('Unknown');
  });

  it("names a project's own key", () => {
    expect(labelFor('project', 'HA', statuses)).toBe('HA');
  });
});

describe('cutKeys', () => {
  it("is the answer's columns, cut by column", () => {
    expect(cutKeys(answer(), 'column')).toEqual([1, 2, 3]);
  });

  it("is the answer's projects, cut by project", () => {
    expect(cutKeys(answer({ projects: ['HA', 'AER'] }), 'project')).toEqual(['HA', 'AER']);
  });
});
