import { describe, expect, it } from 'vitest';
import {
  BARS,
  DEFAULT_HEALTH_WINDOW,
  HEALTH_WINDOWS,
  ageBandPolygons,
  axisSentence,
  bandPolygons,
  barIndexAt,
  barSlots,
  compactReadout,
  flowHref,
  hasStrip,
  issuePeek,
  momentX,
  movedBars,
  oldestIssue,
  oldestLine,
  parseHealthWindow,
  readout,
  splitSentence,
  stepMoment,
  tileLabel,
  waitingSteps,
} from './boardHealth';
import { momentLabel } from './cumulativeFlow';
import type {
  BoardHealth,
  BoardHealthBar,
  BoardHealthCell,
  BoardHealthIssue,
  BoardHealthLane,
  BoardHealthRunners,
} from '../types';

const TO = Date.parse('2026-11-22T15:40:00.000Z');
const BAR_MS = 20 * 60_000;
const FROM = TO - BARS * BAR_MS;

/** Bar `i` of a hand-built window, every number known from `i` alone:
    moved is `i % 5`, split three ways so the parts sum to it; the oldest in
    flight is `600 * (i + 1)` seconds except at 6, 13 and 20, which have none in
    flight; what waits on a person is `i % 3` pull requests and `i % 2`
    questions. */
function bar(i: number, over: Partial<BoardHealthBar> = {}): BoardHealthBar {
  const moved = i % 5;
  const board = moved > 0 && i % 3 === 0 ? 1 : 0;
  const person = moved - board >= 2 ? 1 : 0;
  const waitingPullRequests = i % 3;
  const waitingQuestions = i % 2;

  return {
    start: new Date(FROM + i * BAR_MS).toISOString(),
    end: new Date(FROM + (i + 1) * BAR_MS).toISOString(),
    board,
    person,
    runner: moved - board - person,
    moved,
    waiting: i % 3,
    inFlight: 2 + (i % 4),
    done: i,
    waitingPullRequests,
    waitingQuestions,
    oldestInFlightSeconds: i % 7 === 6 ? null : 600 * (i + 1),
    oldestWaitSeconds: waitingPullRequests + waitingQuestions > 0 ? 7800 : null,
    ages: { healthy: 1, aging: 1, pastNormal: 0, noNormal: 0 },
    ...over,
  };
}

const bars = (over: (i: number) => Partial<BoardHealthBar> = () => ({})): BoardHealthBar[] =>
  Array.from({ length: BARS }, (_, i) => bar(i, over(i)));

const TOTAL = bars().reduce((sum, b) => sum + b.moved, 0);

/** Bar `i` of a hand-built lane, cycling through every cell state and every
    `why` a cell can carry, offset so the four lanes below never agree. */
const CELL_CYCLE: BoardHealthCell[] = [
  { state: 'moved', why: null },
  { state: 'working', why: null },
  { state: 'idle', why: null },
  { state: 'off', why: null },
  { state: 'paused', why: null },
  { state: 'paused', why: 'board' },
  { state: 'down', why: 'out-of-usage' },
  { state: 'down', why: 'not-starting' },
  { state: 'down', why: 'signed-out' },
  { state: 'down', why: null },
  { state: 'let-go', why: 'dropped' },
  { state: 'let-go', why: 'lapsed' },
  { state: 'let-go', why: 'stalled' },
  { state: 'let-go', why: 'errored' },
  { state: 'blank', why: null },
];

function lane(name: string, offset: number, over: Partial<BoardHealthLane> = {}): BoardHealthLane {
  return {
    name,
    up: true,
    paused: false,
    lastSeenAt: new Date(TO).toISOString(),
    cells: Array.from({ length: BARS }, (_, i) => CELL_CYCLE[(i + offset) % CELL_CYCLE.length]),
    ...over,
  };
}

/** Four lanes: alpha and bravo up, charlie up and paused, delta gone - "3 of 4" up, one paused. */
function runners(over: Partial<BoardHealthRunners> = {}): BoardHealthRunners {
  const lanes = [lane('alpha', 0), lane('bravo', 1), lane('charlie', 2, { paused: true }), lane('delta', 3, { up: false })];

  return { up: 3, paused: 1, withLane: lanes.length, lanes, ...over };
}

function answer(over: Partial<BoardHealth> = {}): BoardHealth {
  return {
    from: new Date(FROM).toISOString(),
    to: new Date(TO).toISOString(),
    barMinutes: 20,
    bars: bars(),
    total: TOTAL,
    previousTotal: TOTAL - 16,
    pastNormal: 2,
    hasWorkColumn: true,
    runners: runners(),
    inFlightIssues: [],
    lastWaitedAt: null,
    lastWaitedIsFloor: false,
    ...over,
  };
}

/** A window in which nothing happened anywhere. */
const quiet = (): BoardHealth =>
  answer({
    bars: bars(() => ({
      board: 0,
      runner: 0,
      person: 0,
      moved: 0,
      waiting: 0,
      inFlight: 0,
      done: 0,
      waitingPullRequests: 0,
      waitingQuestions: 0,
      oldestInFlightSeconds: null,
      oldestWaitSeconds: null,
      ages: { healthy: 0, aging: 0, pastNormal: 0, noNormal: 0 },
    })),
    total: 0,
    previousTotal: 0,
    pastNormal: 0,
  });

/** One in-flight issue: HA-101 in In Progress for 4h 10m, whose normal is 2h. */
const issue = (over: Partial<BoardHealthIssue> = {}): BoardHealthIssue => ({
  key: 'HA-101',
  title: 'The oldest one',
  statusId: 3,
  statusName: 'In Progress',
  enteredAt: new Date(TO - 15_000_000).toISOString(),
  seconds: 15_000,
  normalSeconds: 7200,
  band: 'pastNormal',
  ...over,
});

const pairs = (points: string): [number, number][] =>
  points.split(' ').map((p) => p.split(',').map(Number) as [number, number]);

const time = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });

describe('the windows', () => {
  it('offers five lengths and opens on eight hours', () => {
    expect([...HEALTH_WINDOWS]).toEqual(['2h', '4h', '8h', '12h', '24h']);
    expect(DEFAULT_HEALTH_WINDOW).toBe('8h');
  });

  it('reads an offered window as itself and anything else as the default', () => {
    for (const w of HEALTH_WINDOWS) expect(parseHealthWindow(w)).toBe(w);
    for (const raw of [null, undefined, '', '3h', '8H', '1d', 'banana']) expect(parseHealthWindow(raw)).toBe('8h');
  });
});

describe('hasStrip', () => {
  it('is true with bars and false without, leaving no placeholder', () => {
    expect(hasStrip(answer())).toBe(true);
    expect(hasStrip(answer({ bars: [] }))).toBe(false);
  });
});

describe('axisSentence', () => {
  it('says the window end in the reader zone and the bar width as given', () => {
    expect(axisSentence(answer())).toBe(`to ${time(answer().to)} · 20 min bars`);
    expect(axisSentence(answer({ barMinutes: 60 }))).toBe(`to ${time(answer().to)} · 60 min bars`);
  });
});

describe('the shared axis', () => {
  it('cuts the width into twenty-four contiguous slots', () => {
    const slots = barSlots(240);

    expect(slots).toHaveLength(24);
    expect(slots[0]).toEqual({ x: 0, width: 10 });
    expect(slots[23]).toEqual({ x: 230, width: 10 });
    slots.slice(1).forEach((slot, i) => expect(slot.x).toBe(slots[i].x + slots[i].width));
  });

  it('puts a bar index under its own moment and back, for every bar at any width', () => {
    for (const width of [24, 240, 333, 480.5]) {
      for (let i = 0; i < BARS; i += 1) {
        expect(barIndexAt(momentX(i, width), width)).toBe(i);
      }
    }
  });

  it('puts a moment at the slot centre', () => {
    expect(momentX(0, 240)).toBe(5);
    expect(momentX(23, 240)).toBe(235);
  });

  it('clamps the right edge to the last bar and is null outside the strip', () => {
    expect(barIndexAt(0, 240)).toBe(0);
    expect(barIndexAt(240, 240)).toBe(23);
    expect(barIndexAt(10, 240)).toBe(1);
    expect(barIndexAt(-0.1, 240)).toBeNull();
    expect(barIndexAt(240.1, 240)).toBeNull();
    expect(barIndexAt(5, 0)).toBeNull();
    expect(barIndexAt(5, -10)).toBeNull();
    expect(barIndexAt(Number.NaN, 240)).toBeNull();
  });
});

describe('movedBars', () => {
  it('draws one rectangle per bar, the tallest filling the height, each at its own moment', () => {
    const rects = movedBars(answer(), 240, 40);
    const tallest = Math.max(...answer().bars.map((b) => b.moved));

    expect(rects).toHaveLength(24);
    expect(Math.max(...rects.map((r) => r.height))).toBe(40);
    rects.forEach((rect, i) => {
      expect(rect.index).toBe(i);
      expect(rect.x + rect.width / 2).toBeCloseTo(momentX(i, 240), 1);
      expect(rect.y + rect.height).toBeCloseTo(40, 5);
      expect(rect.height).toBeCloseTo((answer().bars[i].moved / tallest) * 40, 1);
    });
  });

  it('gives a bar that moved nothing no height', () => {
    expect(movedBars(answer(), 240, 40)[0].height).toBe(0);
  });

  it('never draws a bar that moved something under one unit high', () => {
    const lopsided = answer({ bars: bars((i) => ({ moved: i === 0 ? 1 : i === 1 ? 1000 : 0 })) });
    const rects = movedBars(lopsided, 240, 40);

    expect(rects[0].height).toBe(1);
    expect(rects[1].height).toBe(40);
    expect(rects[2].height).toBe(0);
  });

  it('is empty for an answer with no bars', () => {
    expect(movedBars(answer({ bars: [] }), 240, 40)).toEqual([]);
  });
});

describe('bandPolygons', () => {
  const W = 240;
  const H = 100;
  const polygons = bandPolygons(answer(), W, H);
  const tallestStack = Math.max(...answer().bars.map((b) => b.done + b.inFlight + b.waiting));
  const valueOf = (y: number) => ((H - y) / H) * tallestStack;

  it('writes each band as its upper edge left to right then its own lower edge back', () => {
    for (const band of [polygons.done, polygons.inFlight, polygons.waiting]) {
      const points = pairs(band);
      expect(points).toHaveLength(48);
      points.slice(0, 24).forEach(([x], i) => expect(x).toBe(momentX(i, W)));
      points.slice(24).forEach(([x], i) => expect(x).toBe(momentX(23 - i, W)));
    }
  });

  it('stacks done, in flight and waiting to the same totals the unstacked counts give', () => {
    const done = pairs(polygons.done).slice(0, 24);
    const flight = pairs(polygons.inFlight).slice(0, 24);
    const top = pairs(polygons.waiting).slice(0, 24);

    answer().bars.forEach((b, i) => {
      expect(valueOf(done[i][1])).toBeCloseTo(b.done, 1);
      expect(valueOf(flight[i][1])).toBeCloseTo(b.done + b.inFlight, 1);
      expect(valueOf(top[i][1])).toBeCloseTo(b.done + b.inFlight + b.waiting, 1);
    });
  });

  it('meets each band on the one below it with no gap, and rests done on the floor', () => {
    expect(pairs(polygons.inFlight).slice(24)).toEqual(pairs(polygons.done).slice(0, 24).reverse());
    expect(pairs(polygons.waiting).slice(24)).toEqual(pairs(polygons.inFlight).slice(0, 24).reverse());
    expect(pairs(polygons.done).slice(24).every(([, y]) => y === H)).toBe(true);
  });

  it('puts the tallest stack at the top', () => {
    const tops = pairs(polygons.waiting).slice(0, 24).map(([, y]) => y);
    expect(Math.min(...tops)).toBe(0);
  });

  it('is empty strings for an answer with no bars', () => {
    expect(bandPolygons(answer({ bars: [] }), W, H)).toEqual({ done: '', inFlight: '', waiting: '' });
  });
});

describe('ageBandPolygons', () => {
  const W = 240;
  const H = 100;
  /** Bar 4 holds 1 past normal, 2 aging and 3 healthy; bar 9 holds 1 aging only; the rest are as `bar` makes them. */
  const ages = (i: number) =>
    i === 4
      ? { healthy: 3, aging: 2, pastNormal: 1, noNormal: 0 }
      : i === 9
        ? { healthy: 0, aging: 1, pastNormal: 0, noNormal: 0 }
        : { healthy: 1, aging: 1, pastNormal: 0, noNormal: 0 };
  const known = answer({ bars: bars((i) => ({ ages: ages(i) })) });
  const polygons = ageBandPolygons(known, W, H);
  const valueOf = (y: number) => ((H - y) / H) * 6;

  it('writes each band as its upper edge left to right then its own lower edge back', () => {
    for (const band of [polygons.pastNormal, polygons.aging, polygons.healthy]) {
      const points = pairs(band);
      expect(points).toHaveLength(48);
      points.slice(0, 24).forEach(([x], i) => expect(x).toBe(momentX(i, W)));
      points.slice(24).forEach(([x], i) => expect(x).toBe(momentX(23 - i, W)));
    }
  });

  it('stacks past normal lowest, aging above it, healthy above that, at a bar with known ages', () => {
    const past = pairs(polygons.pastNormal).slice(0, 24)[4][1];
    const aging = pairs(polygons.aging).slice(0, 24)[4][1];
    const healthy = pairs(polygons.healthy).slice(0, 24)[4][1];

    expect(valueOf(past)).toBeCloseTo(1, 1);
    expect(valueOf(aging)).toBeCloseTo(3, 1);
    expect(valueOf(healthy)).toBeCloseTo(6, 1);
    // Past normal's upper edge is aging's lower edge, and it rests on the floor.
    expect(pairs(polygons.aging).slice(24)).toEqual(pairs(polygons.pastNormal).slice(0, 24).reverse());
    expect(pairs(polygons.healthy).slice(24)).toEqual(pairs(polygons.aging).slice(0, 24).reverse());
    expect(pairs(polygons.pastNormal).slice(24).every(([, y]) => y === H)).toBe(true);
  });

  it('scales to the tallest bar, which touches the top', () => {
    const tops = pairs(polygons.healthy).slice(0, 24).map(([, y]) => y);

    expect(Math.min(...tops)).toBe(0);
    expect(tops[4]).toBe(0);
    expect(tops[0]).toBeGreaterThan(0);
  });

  it('draws a bar with nothing in flight flat', () => {
    const empty = answer({ bars: bars((i) => ({ ages: i === 9 ? { healthy: 0, aging: 0, pastNormal: 0, noNormal: 0 } : ages(i) })) });
    const drawn = ageBandPolygons(empty, W, H);

    for (const band of [drawn.pastNormal, drawn.aging, drawn.healthy]) {
      const points = pairs(band);
      expect(points[9][1]).toBe(points[47 - 9][1]);
    }
  });

  it('leaves the neutral band empty unless some bar has an issue with no normal', () => {
    expect(polygons.noNormal).toBe('');

    const some = answer({ bars: bars((i) => ({ ages: { ...ages(i), noNormal: i === 12 ? 2 : 0 } })) });
    const drawn = ageBandPolygons(some, W, H);

    expect(drawn.noNormal).not.toBe('');
    // It sits on top of healthy.
    expect(pairs(drawn.noNormal).slice(24)).toEqual(pairs(drawn.healthy).slice(0, 24).reverse());
  });

  it('is four empty strings with no bars', () => {
    expect(ageBandPolygons(answer({ bars: [] }), W, H)).toEqual({ pastNormal: '', aging: '', healthy: '', noNormal: '' });
  });

  it('stays finite when nothing is in flight anywhere', () => {
    const strings = Object.values(ageBandPolygons(quiet(), W, H)).join(' ');

    expect(strings).not.toMatch(/NaN|Infinity/);
  });
});

describe('the oldest issue in words', () => {
  it('is the first of the first in the list, or null with none', () => {
    expect(oldestIssue(answer({ inFlightIssues: [issue(), issue({ key: 'HA-102' })] }))?.key).toBe('HA-101');
    expect(oldestIssue(answer())).toBeNull();
  });

  it('says the split from past normal down, and a column with no normal only when there is one', () => {
    expect(splitSentence({ healthy: 9, aging: 3, pastNormal: 2, noNormal: 0 })).toBe('2 past normal · 3 aging · 9 healthy');
    expect(splitSentence({ healthy: 9, aging: 3, pastNormal: 2, noNormal: 1 })).toBe(
      '2 past normal · 3 aging · 9 healthy · 1 no normal yet',
    );
  });

  it('says the key and title, how long it stood in its column, and the normal of the column', () => {
    expect(issuePeek(issue())).toEqual({
      heading: 'HA-101 The oldest one',
      stood: 'Stood 4h 10m in In Progress',
      normal: 'normal 2h',
    });
    expect(issuePeek(issue({ normalSeconds: null })).normal).toBe('no normal yet');
  });
});

describe('oldestLine', () => {
  const W = 240;
  const H = 40;

  it('breaks into separate segments where nothing was in flight, never drawing to zero', () => {
    const { path } = oldestLine(answer(), W, H);

    // Bars 6, 13 and 20 have none in flight: four runs, 24 - 3 points.
    expect(path.match(/M/g)).toHaveLength(4);
    const points = [...path.matchAll(/[ML] (\S+) (\S+)/g)].map((m) => [Number(m[1]), Number(m[2])]);
    expect(points).toHaveLength(21);
    const xs = points.map(([x]) => x);
    for (const gap of [6, 13, 20]) expect(xs).not.toContain(momentX(gap, W));
    expect(points.every(([, y]) => y < H)).toBe(true);
  });

  it('scales to the oldest and gives the last bar as the dot', () => {
    const { last } = oldestLine(answer(), W, H);

    expect(last).toEqual({ x: momentX(23, W), y: 0 });
  });

  it('has no dot when the last bar has nothing in flight', () => {
    const open = answer({ bars: bars((i) => (i === 23 ? { oldestInFlightSeconds: null } : {})) });

    expect(oldestLine(open, W, H).last).toBeNull();
    expect(oldestLine(open, W, H).path).toContain('M');
  });

  it('draws a lone point as a zero-length line so a round cap shows it', () => {
    const lone = answer({ bars: bars((i) => ({ oldestInFlightSeconds: i === 5 ? 600 : null })) });
    const x = momentX(5, W);

    expect(oldestLine(lone, W, H).path).toBe(`M ${x} 0 L ${x} 0`);
  });

  it('is an empty path and no dot with no bars, or none in flight at all', () => {
    expect(oldestLine(answer({ bars: [] }), W, H)).toEqual({ path: '', last: null });
    expect(oldestLine(quiet(), W, H)).toEqual({ path: '', last: null });
  });
});

describe('waitingSteps', () => {
  const W = 240;
  const H = 40;

  it('steps level to each bar then straight to its value, the dot on the last bar', () => {
    const { line, area, last } = waitingSteps(answer(), W, H);
    const counts = answer().bars.map((b) => b.waitingPullRequests + b.waitingQuestions);
    const scale = Math.max(...counts);
    const y = (n: number) => Math.round((H - (n / scale) * H) * 100) / 100;

    expect(line.startsWith(`M ${momentX(0, W)} ${y(counts[0])}`)).toBe(true);
    expect(line).toContain(`H ${momentX(1, W)} V ${y(counts[1])}`);
    expect(line.match(/H/g)).toHaveLength(23);
    expect(last).toEqual({ x: momentX(23, W), y: y(counts[23]) });
    expect(area).toBe(`${line} V ${H} H ${momentX(0, W)} Z`);
  });

  it('counts pull requests and questions together', () => {
    const one = answer({ bars: bars((i) => ({ waitingPullRequests: i === 3 ? 2 : 0, waitingQuestions: i === 3 ? 1 : 0 })) });

    expect(waitingSteps(one, W, H).line).toContain(`H ${momentX(3, W)} V 0`);
  });

  it('is empty strings and no dot with no bars', () => {
    expect(waitingSteps(answer({ bars: [] }), W, H)).toEqual({ line: '', area: '', last: null });
  });
});

describe('an all-zero answer', () => {
  it('yields finite numbers everywhere', () => {
    const q = quiet();
    const strings = [
      ...Object.values(bandPolygons(q, 240, 40)),
      ...Object.values(ageBandPolygons(q, 240, 40)),
      oldestLine(q, 240, 40).path,
      waitingSteps(q, 240, 40).line,
      waitingSteps(q, 240, 40).area,
    ].join(' ');
    const numbers = [
      ...movedBars(q, 240, 40).flatMap((r) => [r.x, r.y, r.width, r.height]),
      ...(waitingSteps(q, 240, 40).last ? Object.values(waitingSteps(q, 240, 40).last!) : []),
    ];

    expect(strings).not.toMatch(/NaN|Infinity/);
    expect(numbers.every(Number.isFinite)).toBe(true);
    expect(movedBars(q, 240, 40).every((r) => r.height === 0)).toBe(true);
  });
});

describe('readout at rest', () => {
  const rest = readout(answer(), null);

  it('moved: the total, and the change from the window before as a difference of counts', () => {
    expect(rest.moved).toMatchObject({
      value: String(TOTAL),
      second: '16 vs prior 8h',
      detail: '',
      tone: 'good',
      glyph: '▲',
      arrow: 'up',
    });
  });

  it('moved: down is amber, never a fault, and flat is plain', () => {
    expect(readout(answer({ previousTotal: TOTAL + 10 }), null).moved).toMatchObject({
      second: '10 vs prior 8h',
      tone: 'warn',
      glyph: '▼',
      arrow: 'down',
    });
    expect(readout(answer({ previousTotal: TOTAL }), null).moved).toMatchObject({
      second: 'same as prior 8h',
      tone: 'neutral',
      glyph: '–',
      arrow: 'flat',
    });
  });

  it('moved: names the window from the bars, not from a setting', () => {
    expect(readout(answer({ barMinutes: 5 }), null).moved.second).toBe('16 vs prior 2h');
    expect(readout(answer({ barMinutes: 60 }), null).moved.second).toBe('16 vs prior 24h');
  });

  it('in flight: the last bar is now', () => {
    expect(rest.inFlight).toMatchObject({ value: '5', second: '2 waiting · 23 done', detail: '', tone: 'neutral', glyph: '' });
  });

  it('oldest in flight: the last bar span, and how many are past normal', () => {
    expect(rest.oldest).toMatchObject({ value: '4h', second: '2 past normal', tone: 'warn', glyph: '⚠' });
  });

  it('oldest in flight: the detail line names the oldest issue by key and column, and the split is spoken', () => {
    const named = readout(answer({ inFlightIssues: [issue(), issue({ key: 'HA-102' })] }), null).oldest;

    expect(named.detail).toBe('HA-101 · In Progress');
    expect(named.spoken).toBe('0 past normal · 1 aging · 1 healthy');
    expect(named).toMatchObject({ value: '4h', second: '2 past normal', tone: 'warn', glyph: '⚠' });
  });

  it('oldest in flight: no issue leaves the detail empty', () => {
    expect(rest.oldest.detail).toBe('');
    expect(readout(answer({ inFlightIssues: [], bars: [] }), null).oldest.detail).toBe('');
  });

  it('oldest in flight: nothing past normal is plain, and none in flight says none', () => {
    expect(readout(answer({ pastNormal: 0 }), null).oldest).toMatchObject({
      second: 'none past normal',
      tone: 'neutral',
      glyph: '',
    });
    expect(readout(answer({ bars: bars((i) => (i === 23 ? { oldestInFlightSeconds: null } : {})) }), null).oldest.value).toBe(
      'none',
    );
  });

  it('waiting on you: the last bar count and the oldest wait', () => {
    expect(rest.waiting).toMatchObject({ value: '3', second: 'oldest 2h 10m', tone: 'neutral', glyph: '' });
  });

  it('waiting on you: nothing waiting at zero', () => {
    const none = answer({
      bars: bars((i) => (i === 23 ? { waitingPullRequests: 0, waitingQuestions: 0, oldestWaitSeconds: null } : {})),
    });

    expect(readout(none, null).waiting).toMatchObject({ value: '0', second: 'nothing waiting' });
  });

  it('runners: how many are up of how many have a lane, and how many are paused', () => {
    expect(rest.runners).toMatchObject({ value: '3 of 4', second: 'up · 1 paused', detail: '', tone: 'neutral', glyph: '' });
  });

  it('runners: "up" alone with none paused, "none up" with no lanes', () => {
    expect(readout(answer({ runners: runners({ paused: 0 }) }), null).runners.second).toBe('up');
    expect(readout(answer({ runners: { up: 0, paused: 0, withLane: 0, lanes: [] } }), null).runners).toMatchObject({
      value: '0 of 0',
      second: 'none up',
    });
  });

  it('reads an index with no bar behind it as rest', () => {
    expect(readout(answer(), 99)).toEqual(rest);
    expect(readout(answer(), -1)).toEqual(rest);
  });

  it('is finite and honest with no bars at all', () => {
    const empty = readout(answer({ bars: [], total: 0, previousTotal: 0, pastNormal: 0 }), null);

    expect(empty.moved.value).toBe('0');
    expect(empty.inFlight.value).toBe('0');
    expect(empty.oldest.value).toBe('none');
    expect(empty.waiting).toMatchObject({ value: '0', second: 'nothing waiting' });
  });
});

describe('compactReadout', () => {
  const full = readout(answer(), null);

  it('gives the same four figures as the full tiles', () => {
    const compact = compactReadout(answer());

    expect(compact.moved.value).toBe(full.moved.value);
    expect(compact.inFlight.value).toBe(full.inFlight.value);
    expect(compact.oldest.value).toBe(full.oldest.value);
    expect(compact.waiting.value).toBe(full.waiting.value);
    expect(compact.oldest.tone).toBe(full.oldest.tone);
  });

  it('shortens each second line of a hand-built window', () => {
    expect(compactReadout(answer())).toEqual({
      moved: { value: String(TOTAL), second: '▲ 16', tone: 'good' },
      inFlight: { value: '5', second: '23 done', tone: 'neutral' },
      oldest: { value: '4h', second: '2 old', tone: 'warn' },
      waiting: { value: '3', second: '2h 10m old', tone: 'neutral' },
      runners: { value: '3', second: 'of 4', tone: 'neutral' },
    });
  });

  it('moved: down keeps its glyph, flat says same', () => {
    expect(compactReadout(answer({ previousTotal: TOTAL + 10 })).moved.second).toBe('▼ 10');
    expect(compactReadout(answer({ previousTotal: TOTAL })).moved.second).toBe('– same');
  });

  it('oldest: none past normal is none old', () => {
    expect(compactReadout(answer({ pastNormal: 0 })).oldest.second).toBe('none old');
  });

  it('oldest: a null oldest keeps the value none, as the full tile does', () => {
    const last = answer({ bars: bars((i) => (i === 23 ? { oldestInFlightSeconds: null } : {})) });

    expect(compactReadout(last).oldest.value).toBe('none');
  });

  it('waiting: zero waiting is none', () => {
    const none = answer({
      bars: bars((i) => (i === 23 ? { waitingPullRequests: 0, waitingQuestions: 0, oldestWaitSeconds: null } : {})),
    });

    expect(compactReadout(none).waiting).toMatchObject({ value: '0', second: 'none' });
  });

  it('waiting: an unknown oldest wait says unknown', () => {
    const unknown = answer({ bars: bars((i) => (i === 23 ? { oldestWaitSeconds: null } : {})) });

    expect(compactReadout(unknown).waiting.second).toBe('unknown');
  });

  it('is finite and honest with no bars at all', () => {
    const empty = compactReadout(answer({ bars: [], total: 0, previousTotal: 0, pastNormal: 0 }));

    expect(empty.oldest).toMatchObject({ value: 'none', second: 'none old' });
    expect(empty.waiting).toMatchObject({ value: '0', second: 'none' });
  });

  it('runners: splits "N of M" into the value and the second line', () => {
    expect(compactReadout(answer({ runners: { up: 0, paused: 0, withLane: 0, lanes: [] } })).runners).toEqual({
      value: '0',
      second: 'of 0',
      tone: 'neutral',
    });
  });
});

describe('readout on a bar', () => {
  const at = (i: number) => momentLabel(bar(i).start, 20);

  it('moved: the bar count, its moment, and all three splits', () => {
    expect(readout(answer(), 8).moved).toMatchObject({
      value: '3',
      second: at(8),
      detail: '2 by runners, 1 by a person, 0 by the board',
      tone: 'neutral',
      glyph: '',
      arrow: null,
    });
  });

  it('moved: the three splits sum to the value on every bar', () => {
    for (let i = 0; i < BARS; i += 1) {
      const moved = readout(answer(), i).moved;
      const [runners, person, board] = (moved.detail.match(/\d+/g) ?? []).map(Number);

      expect(runners + person + board).toBe(Number(moved.value));
      expect(moved.value).toBe(String(bar(i).moved));
    }
  });

  it('in flight: the same three numbers from the bar, with its moment as the detail', () => {
    expect(readout(answer(), 8).inFlight).toMatchObject({
      value: '2',
      second: '2 waiting · 8 done',
      detail: at(8),
    });
  });

  it('oldest in flight: the bar span and its moment, with no warning of the window', () => {
    expect(readout(answer(), 8).oldest).toMatchObject({ value: '1h 30m', second: at(8), tone: 'neutral', glyph: '' });
    expect(readout(answer(), 6).oldest.value).toBe('none');
  });

  it('oldest in flight: the detail on a bar is the split at that bar, and nothing is added to say aloud', () => {
    const withIssue = answer({ inFlightIssues: [issue()], bars: bars((i) => ({ ages: { healthy: 9, aging: 3, pastNormal: i === 8 ? 2 : 0, noNormal: 0 } })) });
    const on = readout(withIssue, 8).oldest;

    expect(on.detail).toBe('2 past normal · 3 aging · 9 healthy');
    expect(on.spoken).toBeUndefined();
  });

  it('waiting on you: the bar count, its moment, and what kind', () => {
    expect(readout(answer(), 8).waiting).toMatchObject({
      value: '2',
      second: at(8),
      detail: '2 pull requests · 0 questions',
    });
    expect(readout(answer(), 3).waiting.detail).toBe('0 pull requests · 1 question');
    expect(readout(answer(), 1).waiting.detail).toBe('1 pull request · 1 question');
    expect(readout(answer(), 5).waiting.detail).toBe('2 pull requests · 1 question');
    expect(readout(answer(), 0).waiting).toMatchObject({ value: '0', detail: 'nothing waiting' });
  });

  it('runners: lanes neither off nor blank, of all lanes, with the moment and how many are paused', () => {
    // At bar 3: alpha (offset 0) is off, bravo (offset 1) and charlie (offset 2) are paused, delta (offset 3) is down.
    expect(readout(answer(), 3).runners).toMatchObject({ value: '3 of 4', second: at(3), detail: '2 paused' });
  });

  it('runners: no detail when nothing is paused at the bar', () => {
    // At bar 8, every lane is down or let-go: all four count as active, none paused.
    expect(readout(answer(), 8).runners).toMatchObject({ value: '4 of 4', detail: '' });
  });
});

describe('tileLabel', () => {
  const rest = readout(answer(), null);

  it('says the moved change in words, readable alone', () => {
    expect(tileLabel('moved', rest.moved)).toBe(`Moved: ${TOTAL}, up 16 against the prior 8h`);
    expect(tileLabel('moved', readout(answer({ previousTotal: TOTAL + 10 }), null).moved)).toBe(
      `Moved: ${TOTAL}, down 10 against the prior 8h`,
    );
    expect(tileLabel('moved', readout(answer({ previousTotal: TOTAL }), null).moved)).toBe(
      `Moved: ${TOTAL}, same as the prior 8h`,
    );
  });

  it('names the other tiles', () => {
    expect(tileLabel('inFlight', rest.inFlight)).toBe('In flight: 5, 2 waiting · 23 done');
    expect(tileLabel('oldest', rest.oldest)).toBe('Oldest in flight: 4h, 2 past normal, 0 past normal · 1 aging · 1 healthy');
    expect(tileLabel('waiting', rest.waiting)).toBe('Waiting on you: 3, oldest 2h 10m');
    expect(tileLabel('runners', rest.runners)).toBe('Runners: 3 of 4, up · 1 paused');
  });

  it('says the hover list last, only when one is given', () => {
    expect(tileLabel('runners', rest.runners)).not.toContain('kestrel');
    expect(tileLabel('runners', rest.runners, 'kestrel idle')).toBe('Runners: 3 of 4, up · 1 paused, kestrel idle');
  });

  it('adds the hover line on a bar', () => {
    const on = readout(answer(), 8);

    expect(tileLabel('moved', on.moved)).toBe(
      `Moved: 3, ${momentLabel(bar(8).start, 20)}, 2 by runners, 1 by a person, 0 by the board`,
    );
    expect(tileLabel('waiting', on.waiting)).toBe(
      `Waiting on you: 2, ${momentLabel(bar(8).start, 20)}, 2 pull requests · 0 questions`,
    );
  });
});

describe('stepMoment', () => {
  it('steps one bar either way and stops at the ends', () => {
    expect(stepMoment(5, 'ArrowLeft', 24)).toBe(4);
    expect(stepMoment(5, 'ArrowRight', 24)).toBe(6);
    expect(stepMoment(0, 'ArrowLeft', 24)).toBe(0);
    expect(stepMoment(23, 'ArrowRight', 24)).toBe(23);
  });

  it('lands on the last bar from ArrowLeft and the first from ArrowRight when none is marked', () => {
    expect(stepMoment(null, 'ArrowLeft', 24)).toBe(23);
    expect(stepMoment(null, 'ArrowRight', 24)).toBe(0);
  });

  it('jumps with Home and End from anywhere', () => {
    expect(stepMoment(null, 'Home', 24)).toBe(0);
    expect(stepMoment(12, 'Home', 24)).toBe(0);
    expect(stepMoment(null, 'End', 24)).toBe(23);
    expect(stepMoment(12, 'End', 24)).toBe(23);
  });

  it('clears the mark on Escape', () => {
    expect(stepMoment(12, 'Escape', 24)).toBeNull();
    expect(stepMoment(null, 'Escape', 24)).toBeNull();
  });

  it('leaves the mark alone for any other key', () => {
    for (const key of ['Tab', 'Enter', ' ', 'a', 'ArrowUp', 'ArrowDown', 'PageUp']) {
      expect(stepMoment(7, key, 24)).toBe(7);
      expect(stepMoment(null, key, 24)).toBeNull();
    }
  });

  it('gives null for a movement key when there are no bars to mark', () => {
    for (const key of ['ArrowLeft', 'ArrowRight', 'Home', 'End']) expect(stepMoment(null, key, 0)).toBeNull();
  });
});

describe('flowHref', () => {
  const from = new Date(FROM).toISOString();
  const to = new Date(TO).toISOString();
  const ends = `from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;

  it('opens the report on the strip two ends, with no scope when there is none', () => {
    expect(flowHref(answer(), [])).toBe(`/reports/cumulative-flow?${ends}`);
  });

  it('carries one scope key', () => {
    expect(flowHref(answer(), ['AER'])).toBe(`/reports/cumulative-flow?scope=AER&${ends}`);
  });

  it('carries two scope keys with the comma written as a comma', () => {
    expect(flowHref(answer(), ['AER', 'FIL'])).toBe(`/reports/cumulative-flow?scope=AER,FIL&${ends}`);
  });

  it('normalises both ends through toISOString', () => {
    const href = flowHref(answer({ from: '2026-11-22T10:00:00+00:00', to: '2026-11-22T18:00:00+00:00' }), []);

    expect(href).toContain(`from=${encodeURIComponent('2026-11-22T10:00:00.000Z')}`);
    expect(href).toContain(`to=${encodeURIComponent('2026-11-22T18:00:00.000Z')}`);
  });
});
