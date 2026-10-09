import { describe, expect, it } from 'vitest';
import {
  SESSION_WINDOW_MS,
  agePhrase,
  batteryLabel,
  hasBattery,
  pace,
  percentLabel,
  rampColor,
  rampInk,
  resetPhrase,
  ringFraction,
  rowPercent,
  rowResetPhrase,
  rowSentence,
  sessionLimit,
  timeGoneFraction,
  usageVars,
  worstRow,
} from './utilization';
import { luminance } from '@hatch/ui';
import type { Utilization, UtilizationLimit } from '../types';

const NOW = new Date('2026-09-07T08:00:00Z');

const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();

const limit = (over: Partial<UtilizationLimit> = {}): UtilizationLimit => ({
  window: 'session',
  label: 'Session',
  percent: 17,
  resetsAt: at(90 * 60_000),
  ...over,
});

const reading = (over: Partial<Utilization> = {}): Utilization => ({
  state: 'ok',
  readAt: at(0),
  limits: [limit()],
  ...over,
});

describe('ringFraction', () => {
  it('is a full ring a whole window out, and empty at the instant of the reset', () => {
    expect(ringFraction(at(SESSION_WINDOW_MS), NOW)).toBe(1);
    expect(ringFraction(at(0), NOW)).toBe(0);
  });

  it('is the share of the window still to run', () => {
    expect(ringFraction(at(SESSION_WINDOW_MS / 2), NOW)).toBeCloseTo(0.5);
    expect(ringFraction(at(SESSION_WINDOW_MS / 4), NOW)).toBeCloseTo(0.25);
  });

  it('clamps past both ends rather than drawing a negative or a wrapping arc', () => {
    expect(ringFraction(at(-60 * 60_000), NOW)).toBe(0);
    expect(ringFraction(at(SESSION_WINDOW_MS * 3), NOW)).toBe(1);
  });

  /* The trap the whole story is built around: the account sends rows with no
     reset instant, and unknown is a third answer rather than a zero. */
  it('is unknown, not zero and not full, when there is nothing to count to', () => {
    expect(ringFraction(null, NOW)).toBeNull();
    expect(ringFraction(undefined, NOW)).toBeNull();
    expect(ringFraction('not an instant', NOW)).toBeNull();
  });
});

describe('timeGoneFraction', () => {
  it('is the share of a seven-day window that has gone, for weekly and weeklyModel alike', () => {
    const week = 7 * 24 * 60 * 60 * 1000;
    expect(timeGoneFraction('weekly', at(week), NOW)).toBeCloseTo(0);
    expect(timeGoneFraction('weekly', at(week / 2), NOW)).toBeCloseTo(0.5);
    expect(timeGoneFraction('weeklyModel', at(0), NOW)).toBeCloseTo(1);
  });

  it('clamps past both ends', () => {
    const week = 7 * 24 * 60 * 60 * 1000;
    expect(timeGoneFraction('weekly', at(-60 * 60_000), NOW)).toBe(1);
    expect(timeGoneFraction('weekly', at(week * 3), NOW)).toBe(0);
  });

  it('is unknown with no reset instant', () => {
    expect(timeGoneFraction('weekly', null, NOW)).toBeNull();
  });

  it('is unknown for a window with no known length - extra, or a word never seen', () => {
    expect(timeGoneFraction('extra', at(1000), NOW)).toBeNull();
    expect(timeGoneFraction('something-new', at(1000), NOW)).toBeNull();
  });
});

describe('resetPhrase', () => {
  it('says hours and minutes', () => {
    expect(resetPhrase(at(80 * 60_000), NOW)).toBe('resets in 1h 20m');
  });

  it('drops the minutes on a whole hour, and the hours below one', () => {
    expect(resetPhrase(at(120 * 60_000), NOW)).toBe('resets in 2h');
    expect(resetPhrase(at(45 * 60_000), NOW)).toBe('resets in 45m');
  });

  /* "resets in 0m" for fifty-nine seconds reads as broken, so the last minute
     is a phrase rather than a number. */
  it('says under a minute for the last minute, and for one already past', () => {
    expect(resetPhrase(at(30_000), NOW)).toBe('resets in under a minute');
    expect(resetPhrase(at(-1), NOW)).toBe('resets in under a minute');
  });

  it('says the reset time is unknown when there is no instant', () => {
    expect(resetPhrase(null, NOW)).toBe('reset time unknown');
    expect(resetPhrase('whenever', NOW)).toBe('reset time unknown');
  });
});

describe('rowPercent', () => {
  it("is the row's own percentage while the window is still running", () => {
    expect(rowPercent(limit({ percent: 42 }), NOW)).toBe(42);
  });

  it('is 0 once the reset instant has passed - the window it described is over', () => {
    expect(rowPercent(limit({ percent: 99, resetsAt: at(-1) }), NOW)).toBe(0);
    expect(rowPercent(limit({ percent: 99, resetsAt: at(0) }), NOW)).toBe(0);
  });

  it('is unaffected by a reset instant still ahead of us', () => {
    expect(rowPercent(limit({ percent: 99, resetsAt: at(1) }), NOW)).toBe(99);
  });
});

describe('pace', () => {
  it('reads identically for a window whose allowance and whose time run out together', () => {
    // 10% used, 10% of the window gone - 90% of it still to run.
    const level10 = pace(limit({ percent: 10, resetsAt: at(SESSION_WINDOW_MS * 0.9) }), NOW);
    // 95% used, 95% of the window gone - 5% of it still to run.
    const level95 = pace(limit({ percent: 95, resetsAt: at(SESSION_WINDOW_MS * 0.05) }), NOW);

    expect(level10).toBeCloseTo(100);
    expect(level95).toBeCloseTo(100);
  });

  it('is 0 with nothing spent, however much of the window has gone', () => {
    expect(pace(limit({ percent: 0, resetsAt: at(SESSION_WINDOW_MS * 0.9) }), NOW)).toBe(0);
    expect(pace(limit({ percent: 0, resetsAt: at(1) }), NOW)).toBe(0);
  });

  it('reads no worse inside the grace window than it would once the floor had actually been reached', () => {
    // 1% of the window gone, well inside the grace floor.
    const insideGrace = pace(limit({ percent: 2, resetsAt: at(SESSION_WINDOW_MS * 0.99) }), NOW);
    // 5% of the window gone, exactly PACE_GRACE.
    const atGrace = pace(limit({ percent: 2, resetsAt: at(SESSION_WINDOW_MS * 0.95) }), NOW);

    expect(insideGrace).toBeCloseTo(40);
    expect(atGrace).toBeCloseTo(40);
  });

  it('reads gone as 1 when the elapsed share is unknown - the extra row', () => {
    const extra = limit({ window: 'extra', percent: 50, resetsAt: null, label: 'Extra usage' });

    expect(pace(extra, NOW)).toBe(50);
  });

  it('is 0 once the reset instant has passed, via rowPercent\'s own rule', () => {
    expect(pace(limit({ percent: 99, resetsAt: at(-1) }), NOW)).toBe(0);
  });
});

describe('rowResetPhrase', () => {
  it('reads as resetPhrase does while the window has not reset', () => {
    expect(rowResetPhrase(limit({ resetsAt: at(80 * 60_000) }), NOW)).toBe('resets in 1h 20m');
  });

  /* The bug this rule exists to close: resetPhrase alone would say "resets in
     under a minute" forever once the instant is behind us. */
  it('says it has reset, not "resets in under a minute", once the instant has passed', () => {
    expect(rowResetPhrase(limit({ resetsAt: at(-1) }), NOW)).toBe('has reset');
    expect(rowResetPhrase(limit({ resetsAt: at(0) }), NOW)).toBe('has reset');
  });

  it('is unknown, not reset, when there is no instant at all', () => {
    expect(rowResetPhrase(limit({ resetsAt: null }), NOW)).toBe('reset time unknown');
  });
});

describe('agePhrase', () => {
  it('says just now inside the first minute', () => {
    expect(agePhrase(at(0), NOW)).toBe('read just now');
    expect(agePhrase(at(-30_000), NOW)).toBe('read just now');
  });

  it('counts minutes, singular and plural', () => {
    expect(agePhrase(at(-60_000), NOW)).toBe('read 1 minute ago');
    expect(agePhrase(at(-4 * 60_000), NOW)).toBe('read 4 minutes ago');
  });

  it('counts hours once the minutes run out', () => {
    expect(agePhrase(at(-60 * 60_000), NOW)).toBe('read 1 hour ago');
    expect(agePhrase(at(-150 * 60_000), NOW)).toBe('read 2 hours ago');
  });

  /* A reading taken a moment ahead of this clock is "just now", not a negative
     age: the server's clock and the browser's are not the same clock. */
  it('does not run backwards for a reading from the future', () => {
    expect(agePhrase(at(30_000), NOW)).toBe('read just now');
  });

  it('says there has never been one when there is no instant', () => {
    expect(agePhrase(null, NOW)).toBe('never read');
  });
});

describe('sessionLimit', () => {
  it('finds the session row wherever the account put it', () => {
    const found = sessionLimit(
      reading({
        limits: [limit({ window: 'weekly', label: 'Weekly' }), limit({ window: 'session', percent: 42 })],
      }),
    );

    expect(found?.percent).toBe(42);
  });

  it('is null when there is no session row, and when there is no reading', () => {
    expect(sessionLimit(reading({ limits: [] }))).toBeNull();
    expect(sessionLimit(null)).toBeNull();
  });
});

describe('rampColor', () => {
  it('is exactly the given colour at each of the five control points', () => {
    expect(rampColor(0)).toBe('#1fd65f');
    expect(rampColor(60)).toBe('#2e9e43');
    expect(rampColor(90)).toBe('#f2c500');
    expect(rampColor(100)).toBe('#f07c00');
    expect(rampColor(150)).toBe('#d32f2f');
  });

  it('gives 151 distinct colours across 0..150, one per whole projected percent', () => {
    const colors = new Set(Array.from({ length: 151 }, (_, percent) => rampColor(percent)));
    expect(colors.size).toBe(151);
  });

  it('clamps below 0 and above 150 rather than extrapolating past the ends', () => {
    expect(rampColor(-10)).toBe(rampColor(0));
    expect(rampColor(160)).toBe(rampColor(150));
  });
});

describe('rampInk', () => {
  it('measures at least 4.5:1 against every one of the 151 colours', () => {
    for (let percent = 0; percent <= 150; percent++) {
      const color = rampColor(percent);
      const ink = rampInk(color);
      const l = luminance(color);
      const inkLuminance = ink === '#ffffff' ? 1 : 0;
      const [hi, lo] = inkLuminance >= l ? [inkLuminance, l] : [l, inkLuminance];
      const ratio = (hi + 0.05) / (lo + 0.05);

      expect(ratio).toBeGreaterThanOrEqual(4.5);
    }
  });
});

describe('usageVars', () => {
  it('hands the stylesheet the ramp colour and its ink', () => {
    expect(usageVars(0)).toEqual({ '--usage-color': '#1fd65f', '--usage-ink': rampInk('#1fd65f') });
  });
});

describe('worstRow', () => {
  it('picks the highest pace among the rows', () => {
    const worst = worstRow(
      reading({
        limits: [limit({ window: 'session', percent: 10 }), limit({ window: 'weekly', percent: 60, label: 'Weekly' })],
      }),
      NOW,
    );

    expect(worst?.window).toBe('weekly');
  });

  it('never lets extra decide, however full it is', () => {
    const worst = worstRow(
      reading({
        limits: [
          limit({ window: 'session', percent: 10 }),
          limit({ window: 'extra', percent: 100, label: 'Extra usage', resetsAt: null }),
        ],
      }),
      NOW,
    );

    expect(worst?.window).toBe('session');
  });

  it('is coloured by whatever a reading with no session row has', () => {
    const worst = worstRow(reading({ limits: [limit({ window: 'weekly', percent: 33, label: 'Weekly' })] }), NOW);

    expect(worst?.window).toBe('weekly');
  });

  it('is null only when the reading is null, or carries nothing but extra', () => {
    expect(worstRow(null, NOW)).toBeNull();
    expect(
      worstRow(reading({ limits: [limit({ window: 'extra', label: 'Extra usage', resetsAt: null })] }), NOW),
    ).toBeNull();
  });

  /* The case that tells pace and percentage apart: percentage alone would
     pick B (70 > 30), but A's pace is far higher because almost none of its
     window has gone. */
  it('picks by pace rather than by percentage when the two disagree', () => {
    const worst = worstRow(
      reading({
        limits: [
          // 10% of the 5h session window gone.
          limit({ window: 'session', percent: 30, resetsAt: at(SESSION_WINDOW_MS * 0.9) }),
          // 95% of the 7-day weekly window gone.
          limit({
            window: 'weekly',
            label: 'Weekly',
            percent: 70,
            resetsAt: at(7 * 24 * 60 * 60 * 1000 * 0.05),
          }),
        ],
      }),
      NOW,
    );

    expect(worst?.window).toBe('session');
  });
});

describe('hasBattery', () => {
  /* The 204: no runner of mine has reported a reading. No element, no
     placeholder, no reserved space - which is the property the whole story is
     built to protect. */
  it('is false only for the 204', () => {
    expect(hasBattery(null)).toBe(false);
    expect(hasBattery(reading())).toBe(true);
    expect(hasBattery(reading({ state: 'stale' }))).toBe(true);
  });
});

describe('percentLabel', () => {
  it('states the percentage as a whole number', () => {
    expect(percentLabel(limit({ percent: 17 }))).toBe('17%');
  });

  it('is an em dash when there is no reading behind it', () => {
    expect(percentLabel(null)).toBe('—');
  });
});

describe('rowSentence', () => {
  it('says the name, the percent used, the percent of the window gone, the pace, and when it resets', () => {
    const week = 7 * 24 * 60 * 60 * 1000;
    const row = limit({ window: 'weekly', label: 'Weekly', percent: 40, resetsAt: at(week / 2) });

    // pace: 0.4 / max(0.5, 0.05) = 0.8 -> 80%, nearest control point 90.
    expect(rowSentence(row, NOW)).toBe('Weekly: 40% used, 50% of the window gone, just ahead of the reset, resets in 84h');
  });

  it('leaves out the window-gone clause for a row with no known length, but still says the pace', () => {
    const extra = limit({ window: 'extra', label: 'Extra usage', percent: 12, resetsAt: null });

    // gone defaults to 1 with no known window length, so pace is 12%.
    expect(rowSentence(extra, NOW)).toBe('Extra usage: 12% used, allowance to spare, reset time unknown');
  });

  it('says a row past its reset has reset, draws at 0, and paces at 0', () => {
    const past = limit({ window: 'session', label: 'Session', percent: 88, resetsAt: at(-1) });

    expect(rowSentence(past, NOW)).toBe('Session: 0% used, 100% of the window gone, allowance to spare, has reset');
  });
});

describe('batteryLabel', () => {
  it('says the number, the pace, and the time left', () => {
    // default limit: 17% used, 90m of the 5h window left -> 70% gone, pace ~24.3% -> nearest 0.
    expect(batteryLabel(reading(), NOW)).toBe('Claude session usage 17%, allowance to spare, resets in 1h 30m');
  });

  it('says the reset is unknown when the row carries no instant', () => {
    // no resetsAt -> gone defaults to 1, pace 17%.
    expect(batteryLabel(reading({ limits: [limit({ resetsAt: null })] }), NOW)).toBe(
      'Claude session usage 17%, allowance to spare, reset time unknown',
    );
  });

  it('carries the age of a stale reading, so an old number says it is old', () => {
    const stale = reading({ state: 'stale', readAt: at(-12 * 60_000) });

    expect(batteryLabel(stale, NOW)).toBe('Claude session usage 17%, allowance to spare, resets in 1h 30m (read 12 minutes ago)');
  });

  it('reads as unknown when there is no battery at all', () => {
    expect(batteryLabel(null, NOW)).toBe('Claude usage unknown — no runner of mine has reported one');
  });

  /* A runner could report a weekly window alone, between sessions - still a
     reading, just not one with a session row to draw the glyph from. */
  it('says the session window has not been reported when the reading carries no session row', () => {
    const weeklyOnly = reading({ limits: [limit({ window: 'weekly', label: 'Weekly' })] });

    expect(batteryLabel(weeklyOnly, NOW)).toBe('Claude session usage unknown — the session window has not been reported');
  });

  /* Criterion 2.3: the battery's accessible name says which window the colour
     came from whenever it is not the session's. */
  it('names the other window when it is the one colouring the battery', () => {
    const worseWeekly = reading({
      limits: [limit({ window: 'session', percent: 17 }), limit({ window: 'weekly', percent: 91, label: 'Weekly' })],
    });

    // weekly pace (default resetsAt, 90m of 7d gone) ~91.8% -> nearest 90.
    expect(batteryLabel(worseWeekly, NOW)).toBe(
      'Claude session usage 17%, just ahead of the reset, resets in 1h 30m, coloured by Weekly at 91%',
    );
  });

  it('says nothing extra when the session is already the worst', () => {
    const sessionWorst = reading({
      limits: [limit({ window: 'session', percent: 91 }), limit({ window: 'weekly', percent: 10, label: 'Weekly' })],
    });

    // session pace: 0.91 / max(90m/5h = 0.3, 0.05) = 0.91/0.3 ~= 303% -> clamped to 150.
    expect(batteryLabel(sessionWorst, NOW)).toBe('Claude session usage 91%, spending ahead of the reset, resets in 1h 30m');
  });
});

describe('a model-scoped row', () => {
  /* The label is the account's own word for the model and Hatch renders it as
     given - which is why no model name is written down in this repository, and
     why the name in this test is one that appears nowhere else. */
  it('is named by the label the server derived, whatever it says', () => {
    const scoped = limit({ window: 'weeklyModel', label: 'Some Model Nobody Here Names', resetsAt: null });
    const withScoped = reading({ limits: [limit(), scoped] });

    expect(withScoped.limits[1].label).toBe('Some Model Nobody Here Names');
    expect(resetPhrase(withScoped.limits[1].resetsAt, NOW)).toBe('reset time unknown');
    // Still rendered, still in the account's order, and not filtered by tone.
    expect(sessionLimit(withScoped)?.window).toBe('session');
  });
});
