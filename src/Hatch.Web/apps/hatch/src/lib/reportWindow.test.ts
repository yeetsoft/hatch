import { describe, expect, it } from 'vitest';
import {
  DEFAULT_DAYS,
  WINDOW_PRESETS,
  parsePreset,
  presetOf,
  resizeWindow,
  resolveWindow,
  sameWindow,
  shiftWindow,
  windowEndingToday,
  withWindow,
} from './reportWindow';
import { makeScale, tickUnit } from './timeline';

const NOW = new Date('2026-10-09T12:00:00Z');
const DAY_MS = 86_400_000;

const params = (query: string) => new URLSearchParams(query);

/** A scale `ms` wide, from a fixed start. */
const widthScale = (ms: number) => {
  const from = '2026-10-01T00:00:00.000Z';

  return makeScale(from, new Date(Date.parse(from) + ms).toISOString());
};

/** The ninety days ending at `to`, the default window's `from`. */
const ninetyBefore = (to: string) => new Date(Date.parse(to) - DEFAULT_DAYS * DAY_MS).toISOString();


/** A window `count` days long ending at `to`. */
const ending = (to: string, count: number) => ({
  from: new Date(Date.parse(to) - count * DAY_MS).toISOString(),
  to,
});

/** A window `ms` long ending at `to`. */
const endingMs = (to: string, ms: number) => ({
  from: new Date(Date.parse(to) - ms).toISOString(),
  to,
});

const D = '2026-10-10T00:00:00.000Z';
const before = (count: number, from = D) => new Date(Date.parse(from) - count * DAY_MS).toISOString();

describe('resolveWindow', () => {
  it('defaults to ninety days ending at the next midnight at offset 0', () => {
    const window = resolveWindow(params(''), NOW, 0);

    expect(window.to).toBe('2026-10-10T00:00:00.000Z');
    expect(window.from).toBe(ninetyBefore(window.to));
    expect(window.from).toBe('2026-07-12T00:00:00.000Z');
  });

  it("ends at the reader's midnight, not the runtime's, at offset 600", () => {
    const window = resolveWindow(params(''), NOW, 600);

    expect(window.to).toBe('2026-10-09T14:00:00.000Z');
    expect(window.from).toBe(ninetyBefore(window.to));
  });

  it('puts `to` strictly after now when now is exactly a midnight', () => {
    expect(resolveWindow(params(''), new Date('2026-10-10T00:00:00Z'), 0).to).toBe(
      '2026-10-11T00:00:00.000Z',
    );
    expect(resolveWindow(params(''), new Date('2026-10-09T14:00:00Z'), 600).to).toBe(
      '2026-10-10T14:00:00.000Z',
    );
  });

  it('uses an explicit pair, normalised', () => {
    expect(resolveWindow(params('from=2026-10-01&to=2026-10-20T06:30:00%2B02:00'), NOW, 600)).toEqual({
      from: '2026-10-01T00:00:00.000Z',
      to: '2026-10-20T04:30:00.000Z',
    });
  });

  const fallback = resolveWindow(params(''), NOW, 0);

  it.each([
    ['from alone', 'from=2026-10-01'],
    ['to alone', 'to=2026-10-20'],
    ['a blank value', 'from=2026-10-01&to=%20'],
    ['an unparseable from', 'from=yesterday&to=2026-10-20'],
    ['an unparseable to', 'from=2026-10-01&to=soon'],
    ['an inverted pair', 'from=2026-10-20&to=2026-10-01'],
    ['an equal pair', 'from=2026-10-01&to=2026-10-01T00:00:00Z'],
  ])('falls back to the default for %s', (_, query) => {
    expect(resolveWindow(params(query), NOW, 0)).toEqual(fallback);
  });
  it.each([
    ['all', 36_525 * DAY_MS],
    ['1y', 365 * DAY_MS],
    ['6m', 183 * DAY_MS],
    ['3m', 90 * DAY_MS],
    ['1m', 30 * DAY_MS],
    ['2w', 14 * DAY_MS],
    ['1w', 7 * DAY_MS],
  ])('ends at the next midnight and reaches back the preset for range=%s', (key, ms) => {
    const window = resolveWindow(params(`range=${key}`), NOW, 0);

    expect(window.to).toBe('2026-10-10T00:00:00.000Z');
    expect(window.from).toBe(new Date(Date.parse(window.to) - ms).toISOString());
  });

  it.each([
    ['1h', 3_600_000],
    ['2h', 2 * 3_600_000],
    ['6h', 6 * 3_600_000],
    ['12h', 12 * 3_600_000],
    ['1d', DAY_MS],
  ])('ends at now and reaches back the preset for range=%s', (key, ms) => {
    const window = resolveWindow(params(`range=${key}`), NOW, 0);

    expect(window.to).toBe(NOW.toISOString());
    expect(window.from).toBe(new Date(NOW.getTime() - ms).toISOString());
  });

  it('is as wide as defaultDays when no preset is named, and range still beats it', () => {
    const window = resolveWindow(params(''), NOW, 0, 14);

    expect(window.to).toBe('2026-10-10T00:00:00.000Z');
    expect(window.from).toBe(before(14));
    expect(resolveWindow(params('range=1w'), NOW, 0, 14).from).toBe(before(7));
  });

  it("ends a range at the reader's midnight at offset 600", () => {
    expect(resolveWindow(params('range=1y'), NOW, 600).to).toBe('2026-10-09T14:00:00.000Z');
  });

  it.each([['range=bogus'], ['range=']])('ignores %s', (query) => {
    expect(resolveWindow(params(query), NOW, 0)).toEqual(fallback);
  });

  it('lets a valid pair beat range', () => {
    expect(resolveWindow(params('range=1y&from=2026-10-01&to=2026-10-20'), NOW, 0)).toEqual({
      from: '2026-10-01T00:00:00.000Z',
      to: '2026-10-20T00:00:00.000Z',
    });
  });

  it('uses range when the pair is only half there', () => {
    const window = resolveWindow(params('range=1y&from=2026-10-01'), NOW, 0);

    expect(window.from).toBe(new Date(Date.parse(window.to) - 365 * DAY_MS).toISOString());
  });
});

describe('WINDOW_PRESETS', () => {
  it('has the default as its quarter', () => {
    expect(WINDOW_PRESETS.find((preset) => preset.key === '3m')?.ms).toBe(DEFAULT_DAYS * DAY_MS);
  });

  it('is ticked by ten minutes, half hour, half hour, hour, three hours, day, day, day, week, week, month, month', () => {
    expect(WINDOW_PRESETS.map((preset) => tickUnit(widthScale(preset.ms)))).toEqual([
      'tenMinutes',
      'halfHour',
      'halfHour',
      'hour',
      'threeHours',
      'day',
      'day',
      'day',
      'week',
      'week',
      'month',
      'month',
    ]);
  });
});

describe('parsePreset', () => {
  it.each(['1h', '2h', '6h', '12h', '1d', '1w', '2w', '1m', '3m', '6m', '1y', 'all'])('reads %s', (key) => {
    expect(parsePreset(key)).toBe(key);
  });

  it.each([['3M'], [''], [null], ['24h']])('reads %j as null', (raw) => {
    expect(parsePreset(raw)).toBeNull();
  });
});

describe('shiftWindow', () => {
  const w = ending(D, 90);

  it('moves half the length earlier', () => {
    const shifted = shiftWindow(w, -1);

    expect(shifted.to).toBe('2026-08-26T00:00:00.000Z');
    expect(Date.parse(shifted.to) - Date.parse(shifted.from)).toBe(90 * DAY_MS);
  });

  it('moves half the length later', () => {
    expect(shiftWindow(w, 1).to).toBe('2026-11-24T00:00:00.000Z');
  });

  it('is undone by the opposite shift', () => {
    expect(shiftWindow(shiftWindow(w, -1), 1)).toEqual(w);
  });

  it('moves a fortnight by a week', () => {
    expect(shiftWindow(ending(D, 14), -1).to).toBe(before(7));
  });
});

describe('resizeWindow', () => {
  const w = ending(D, 90);

  it('widens backwards from a fixed end', () => {
    expect(resizeWindow(w, '6m', NOW)).toEqual({ from: before(183), to: D });
  });

  it('narrows backwards from a fixed end', () => {
    expect(resizeWindow(w, '2w', NOW)).toEqual({ from: before(14), to: D });
  });

  it('is a no-op at the current length', () => {
    expect(resizeWindow(w, '3m', NOW)).toEqual(w);
  });

  it('ends a now-anchored preset at now when the fixed end is later than now', () => {
    expect(resizeWindow(w, '1h', NOW)).toEqual({
      from: new Date(NOW.getTime() - 3_600_000).toISOString(),
      to: NOW.toISOString(),
    });
  });

  it('keeps `to` for a past window moved to a now-anchored preset', () => {
    const past = ending(before(100), 1);

    expect(resizeWindow(past, '1d', NOW).to).toBe(past.to);
  });

  it('keeps a window already ending at now moved to another now-anchored preset', () => {
    const atNow = endingMs(NOW.toISOString(), 3_600_000);

    expect(resizeWindow(atNow, '1d', NOW).to).toBe(NOW.toISOString());
  });
});

describe('windowEndingToday', () => {
  const old = ending(before(30), 90);

  it('ends at the next midnight and keeps the length at offset 0', () => {
    const moved = windowEndingToday(old, NOW, 0);

    expect(moved.to).toBe(D);
    expect(Date.parse(moved.to) - Date.parse(moved.from)).toBe(90 * DAY_MS);
  });

  it("ends at the reader's midnight at offset 600", () => {
    expect(windowEndingToday(old, NOW, 600).to).toBe('2026-10-09T14:00:00.000Z');
  });

  it('changes nothing for a window already ending there', () => {
    const w = ending(D, 90);

    expect(sameWindow(windowEndingToday(w, NOW, 0), w)).toBe(true);
  });

  it('gives the midnight after when now is exactly a midnight', () => {
    expect(windowEndingToday(old, new Date(D), 0).to).toBe('2026-10-11T00:00:00.000Z');
  });

  it('ends at now for a window under two days long', () => {
    const twoHour = endingMs(before(30), 2 * 3_600_000);

    expect(windowEndingToday(twoHour, NOW, 0).to).toBe(NOW.toISOString());
  });

  it('ends at the next midnight for a week-long window', () => {
    const week = ending(before(30), 7);

    expect(windowEndingToday(week, NOW, 0).to).toBe(D);
  });

  it("ends a week-long window at the reader's midnight at offset 600", () => {
    const week = ending(before(30), 7);

    expect(windowEndingToday(week, NOW, 600).to).toBe('2026-10-09T14:00:00.000Z');
  });
});

describe('presetOf', () => {
  it.each(WINDOW_PRESETS)('names the $key window', ({ key, ms }) => {
    expect(presetOf(endingMs(D, ms))).toBe(key);
  });

  it('absorbs an hour lost to DST', () => {
    const w = ending(D, 90);

    expect(presetOf({ from: new Date(Date.parse(w.from) + 3_600_000).toISOString(), to: w.to })).toBe('3m');
  });

  it('is null for a width that is no preset', () => {
    expect(presetOf(ending(D, 45))).toBeNull();
  });

  it('is null for ninety minutes', () => {
    expect(presetOf(endingMs(D, 90 * 60_000))).toBeNull();
  });
});

describe('sameWindow', () => {
  it('compares both ends', () => {
    expect(sameWindow(ending(D, 90), ending(D, 90))).toBe(true);
    expect(sameWindow(ending(D, 90), ending(D, 30))).toBe(false);
    expect(sameWindow(ending(D, 90), ending(before(1), 90))).toBe(false);
  });
});

describe('withWindow', () => {
  const w = ending(D, 30);

  it('sets both ends, drops range and keeps the rest', () => {
    const next = withWindow(params('range=1y&project=3'), w);

    expect(next.get('from')).toBe(w.from);
    expect(next.get('to')).toBe(w.to);
    expect(next.has('range')).toBe(false);
    expect(next.get('project')).toBe('3');
  });

  it('does not mutate its input', () => {
    const input = params('range=1y&project=3');
    withWindow(input, w);

    expect(input.toString()).toBe('range=1y&project=3');
  });
});

