import { describe, expect, it } from 'vitest';
import { BARS, momentX, splitSentence, waitingSteps } from './boardHealth';
import {
  MARK_PATTERN,
  axisTicks,
  bandCounts,
  dialogTitle,
  legend,
  markedWords,
  movedSummary,
  movedTotals,
  oldestWaitWords,
  scaleTop,
  stackedMovedBars,
  waitingSplit,
} from './boardHealthDialog';
import type { Tile } from './boardHealth';
import { sampleBar, sampleHealth } from './boardHealthSample';
import { formatSpan } from './cardAge';
import { momentLabel } from './cumulativeFlow';
import type { BoardHealth, BoardHealthBar } from '../types';

/** The sample with bar `i` changed. */
const withBar = (answer: BoardHealth, i: number, over: Partial<BoardHealthBar>): BoardHealth => ({
  ...answer,
  bars: answer.bars.map((bar, at) => (at === i ? { ...bar, ...over } : bar)),
});

const TILES: Tile[] = ['moved', 'inFlight', 'oldest', 'waiting'];

describe('dialogTitle', () => {
  it('is the tile\'s own label and the window', () => {
    const answer = sampleHealth();

    expect(dialogTitle('moved', answer)).toBe('Moved, last 8h');
    expect(dialogTitle('inFlight', answer)).toBe('In flight, last 8h');
    expect(dialogTitle('oldest', answer)).toBe('Oldest in flight, last 8h');
    expect(dialogTitle('waiting', answer)).toBe('Waiting on you, last 8h');
  });

  it('follows the answer\'s window, not the button that was pressed', () => {
    expect(dialogTitle('moved', sampleHealth({ barMinutes: 10 }))).toBe('Moved, last 4h');
    expect(dialogTitle('moved', sampleHealth({ barMinutes: 60 }))).toBe('Moved, last 24h');
  });
});

describe('stackedMovedBars', () => {
  it('stacks runners at the bottom, then a person, then the board', () => {
    const answer = withBar(sampleHealth(), 4, { runner: 2, person: 1, board: 1, moved: 4 });
    const [bar] = stackedMovedBars(answer, 480, 200).filter((b) => b.index === 4);

    expect(bar.segments.map((s) => s.source)).toEqual(['runner', 'person', 'board']);
    // The first is lowest: its y is the largest, and each next one sits on the last.
    expect(bar.segments[0].y + bar.segments[0].height).toBeCloseTo(200, 1);
    expect(bar.segments[1].y + bar.segments[1].height).toBeCloseTo(bar.segments[0].y, 1);
    expect(bar.segments[2].y + bar.segments[2].height).toBeCloseTo(bar.segments[1].y, 1);
  });

  it('scales every height to the tallest bar\'s moved', () => {
    const bars = stackedMovedBars(sampleHealth(), 480, 200);
    const tall = bars[4]; // moved 4, the tallest in the sample
    const top = tall.segments[tall.segments.length - 1];

    expect(top.y).toBeCloseTo(0, 1);
    expect(tall.segments.reduce((sum, s) => sum + s.height, 0)).toBeCloseTo(200, 1);
    const short = bars[1]; // moved 1
    expect(short.segments.reduce((sum, s) => sum + s.height, 0)).toBeCloseTo(50, 1);
  });

  it('leaves a source that moved nothing out, and keeps the bar', () => {
    const bars = stackedMovedBars(sampleHealth(), 480, 200);

    expect(bars).toHaveLength(BARS);
    expect(bars[1].segments.map((s) => s.source)).toEqual(['person']);
    expect(bars[0].segments).toEqual([]);
  });

  it('never draws a segment under one unit high, and stays in the box', () => {
    const answer = withBar(sampleHealth(), 7, { runner: 200, person: 1, board: 1, moved: 202 });
    const bar = stackedMovedBars(answer, 480, 100)[7];

    for (const segment of bar.segments) expect(segment.height).toBeGreaterThanOrEqual(1);
    expect(Math.min(...bar.segments.map((s) => s.y))).toBeGreaterThanOrEqual(0);
    expect(bar.segments.reduce((sum, s) => sum + s.height, 0)).toBeLessThanOrEqual(100.01);
  });

  it('draws nothing, and divides by one, for an all-zero window', () => {
    const quiet = sampleHealth({
      bars: sampleHealth().bars.map((bar) => ({ ...bar, runner: 0, person: 0, board: 0, moved: 0 })),
    });
    const bars = stackedMovedBars(quiet, 480, 200);

    expect(bars).toHaveLength(BARS);
    for (const bar of bars) expect(bar.segments).toEqual([]);
  });

  it('gives no bars for an answer with none', () => {
    expect(stackedMovedBars(sampleHealth({ bars: [] }), 480, 200)).toEqual([]);
  });

  it('puts each bar on the shared axis, centred in its slot', () => {
    const bars = stackedMovedBars(sampleHealth(), 480, 200);

    for (const bar of bars) expect(bar.x + bar.width / 2).toBeCloseTo(momentX(bar.index, 480), 1);
  });
});

describe('movedTotals', () => {
  it('sums each source over the bars, and the total of the moves', () => {
    const answer = sampleHealth();
    const totals = movedTotals(answer);

    expect(totals.runner).toBe(answer.bars.reduce((sum, bar) => sum + bar.runner, 0));
    expect(totals.person).toBe(answer.bars.reduce((sum, bar) => sum + bar.person, 0));
    expect(totals.board).toBe(0);
    expect(totals.total).toBe(answer.total);
    expect(totals.runner + totals.person + totals.board).toBe(totals.total);
  });

  it('carries the window before as one figure', () => {
    expect(movedTotals(sampleHealth({ previousTotal: 31 })).previousTotal).toBe(31);
  });

  it('writes the five rows', () => {
    const rows = movedSummary(sampleHealth({ previousTotal: 31 }));

    expect(rows.map((row) => row.label)).toEqual([
      'By runners',
      'By a person',
      'By the board',
      'All, last 8h',
      'All, the 8h before',
    ]);
    expect(rows[4].value).toBe('31');
  });
});

describe('waitingSplit', () => {
  it('puts the questions\' upper edge exactly on the tile\'s own line', () => {
    const answer = sampleHealth();

    for (const [w, h] of [[480, 200], [240, 40], [333, 91]]) {
      expect(waitingSplit(answer, w, h).questions.line).toBe(waitingSteps(answer, w, h).line);
    }
  });

  it('puts the pull requests\' edge under it, and closes each area', () => {
    const split = waitingSplit(sampleHealth(), 480, 200);

    expect(split.pullRequests.line).not.toBe(split.questions.line);
    expect(split.pullRequests.area).toMatch(/Z$/);
    expect(split.questions.area).toMatch(/Z$/);
    expect(split.pullRequests.area.startsWith(split.pullRequests.line)).toBe(true);
    expect(split.questions.area.startsWith(split.questions.line)).toBe(true);
  });

  it('closes the pull requests down to the floor, as the tile\'s area does', () => {
    const answer = sampleHealth();

    expect(waitingSplit(answer, 480, 200).pullRequests.area).toMatch(/ V 200 H [\d.]+ Z$/);
  });

  it('closes the questions back along the pull requests\' edge, not the floor', () => {
    const answer = sampleHealth();
    const area = waitingSplit(answer, 480, 200).questions.area;

    // One step back per bar after the first, ending where the first bar began.
    expect((area.match(/ H /g) ?? []).length).toBe(2 * (BARS - 1));
    expect(area).toContain(` H ${momentX(0, 480)} V `);
  });

  it('is flat where nothing waits, so the two edges meet the floor', () => {
    const quiet = sampleHealth({
      bars: sampleHealth().bars.map((bar) => ({ ...bar, waitingPullRequests: 0, waitingQuestions: 0 })),
    });
    const split = waitingSplit(quiet, 480, 200);

    expect(split.questions.line).toBe(split.pullRequests.line);
    expect(split.questions.line).toMatch(/^M [\d.]+ 200( H [\d.]+ V 200)+$/);
  });

  it('gives empty strings for an answer with no bars', () => {
    expect(waitingSplit(sampleHealth({ bars: [] }), 480, 200)).toEqual({
      pullRequests: { line: '', area: '' },
      questions: { line: '', area: '' },
    });
  });
});

describe('axisTicks', () => {
  const time = (at: string | number): string =>
    new Date(at).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });

  it('labels the start of bars 0, 6, 12 and 18, and the window\'s end at the right edge', () => {
    const answer = sampleHealth();
    const ticks = axisTicks(answer, 480, 'en-GB');

    expect(ticks.map((tick) => tick.x)).toEqual([0, 120, 240, 360, 480]);
    expect(ticks.map((tick) => tick.label)).toEqual([
      time(answer.bars[0].start),
      time(answer.bars[6].start),
      time(answer.bars[12].start),
      time(answer.bars[18].start),
      time(answer.to),
    ]);
  });

  it('writes the times in the form the axis sentence does', () => {
    expect(axisTicks(sampleHealth(), 480, 'en-GB').every((tick) => /^\d\d:\d\d$/.test(tick.label))).toBe(true);
  });

  it('gives none for an answer with no bars', () => {
    expect(axisTicks(sampleHealth({ bars: [] }), 480)).toEqual([]);
  });
});

describe('scaleTop', () => {
  it('is the tallest moved, for Moved', () => {
    expect(scaleTop('moved', sampleHealth())).toBe('4');
  });

  it('is the tallest done + in flight + waiting, for In flight', () => {
    const answer = sampleHealth();
    const tallest = Math.max(...answer.bars.map((bar) => bar.done + bar.inFlight + bar.waiting));

    expect(scaleTop('inFlight', answer)).toBe(String(tallest));
  });

  it('is the tallest stack of age bands, a count of issues, for Oldest in flight', () => {
    expect(scaleTop('oldest', sampleHealth())).toBe('5'); // each bar totals 2 + i % 4
  });

  it('counts the grey band in the scale, as the bands do', () => {
    expect(scaleTop('oldest', withBar(sampleHealth(), 2, { ages: { pastNormal: 1, aging: 1, healthy: 1, noNormal: 6 } }))).toBe('9');
  });

  it('is the tallest waiting count, for Waiting on you', () => {
    expect(scaleTop('waiting', sampleHealth())).toBe('3'); // 2 pull requests + 1 question
  });

  it('never reads under one, so a quiet window divides by one', () => {
    const quiet = sampleHealth({
      bars: sampleHealth().bars.map((bar) => ({ ...bar, moved: 0, waitingPullRequests: 0, waitingQuestions: 0 })),
    });

    expect(scaleTop('moved', quiet)).toBe('1');
    expect(scaleTop('waiting', quiet)).toBe('1');
  });

  it('is empty for Runners, which has no value scale', () => {
    expect(scaleTop('runners', sampleHealth())).toBe('');
  });
});

describe('legend', () => {
  it('names Moved\'s three sources, top to bottom', () => {
    expect(legend('moved')).toEqual([
      { key: 'board', label: 'The board' },
      { key: 'person', label: 'A person' },
      { key: 'runner', label: 'Runners' },
    ]);
  });

  it('names In flight\'s three bands, top to bottom', () => {
    expect(legend('inFlight').map((entry) => entry.label)).toEqual(['Waiting', 'In flight', 'Done']);
  });

  it('names Oldest\'s three age bands, top to bottom, with no answer or none grey', () => {
    expect(legend('oldest').map((entry) => entry.key)).toEqual(['healthy', 'aging', 'pastNormal']);
    expect(legend('oldest', sampleHealth()).map((entry) => entry.key)).toEqual(['healthy', 'aging', 'pastNormal']);
    expect(legend('oldest', sampleHealth()).map((entry) => entry.label)).toEqual(['Healthy', 'Aging', 'Past normal']);
  });

  it('adds the grey band on top only when some bar has an issue with no normal yet', () => {
    const grey = withBar(sampleHealth(), 4, { ages: { pastNormal: 0, aging: 0, healthy: 1, noNormal: 2 } });

    expect(legend('oldest', grey).map((entry) => entry.key)).toEqual(['noNormal', 'healthy', 'aging', 'pastNormal']);
    expect(legend('oldest', grey)[0].label).toBe('No normal yet');
  });

  it('names Waiting\'s two areas, top to bottom', () => {
    expect(legend('waiting').map((entry) => entry.label)).toEqual(['Questions', 'Pull requests']);
  });

  it('has no chart legend for Runners, whose seven lines are the lane states', () => {
    expect(legend('runners')).toEqual([]);
  });

  it('tells the stacked marks of one chart apart by pattern, not shade alone', () => {
    const grey = withBar(sampleHealth(), 4, { ages: { pastNormal: 0, aging: 0, healthy: 1, noNormal: 2 } });

    for (const [tile, answer] of [['moved'], ['waiting'], ['oldest', grey]] as [Tile, BoardHealth?][]) {
      const patterns = legend(tile, answer).map((entry) => MARK_PATTERN[entry.key]);

      expect(patterns.every(Boolean)).toBe(true);
      expect(new Set(patterns).size).toBe(patterns.length);
    }
  });
});

describe('bandCounts', () => {
  it('gives each band\'s count at the marked bar, top band first', () => {
    const bar = sampleBar(5);

    expect(bandCounts(sampleHealth(), 5)).toEqual([
      { key: 'waiting', label: 'Waiting', value: String(bar.waiting) },
      { key: 'inFlight', label: 'In flight', value: String(bar.inFlight) },
      { key: 'done', label: 'Done', value: String(bar.done) },
    ]);
  });

  it('reads the last bar, which is now, at rest', () => {
    expect(bandCounts(sampleHealth(), null)[2].value).toBe(String(BARS - 1));
  });

  it('gives none for an answer with no bars', () => {
    expect(bandCounts(sampleHealth({ bars: [] }), null)).toEqual([]);
  });
});

describe('oldestWaitWords', () => {
  it('says how long the oldest has waited, from the last bar', () => {
    const answer = withBar(sampleHealth(), BARS - 1, { waitingPullRequests: 1, waitingQuestions: 0, oldestWaitSeconds: 7800 });

    expect(oldestWaitWords(answer)).toBe('oldest waited 2h 10m');
  });

  it('says so when nothing waits, and when the age is not known', () => {
    expect(oldestWaitWords(withBar(sampleHealth(), BARS - 1, { waitingPullRequests: 0, waitingQuestions: 0 }))).toBe(
      'nothing waiting',
    );
    expect(
      oldestWaitWords(withBar(sampleHealth(), BARS - 1, { waitingPullRequests: 1, waitingQuestions: 0, oldestWaitSeconds: null })),
    ).toBe('oldest wait unknown');
  });
});

describe('markedWords', () => {
  const locale = 'en-GB';

  it('gives Moved\'s totals at rest', () => {
    const answer = sampleHealth({ previousTotal: 31 });
    const totals = movedTotals(answer);

    expect(markedWords('moved', answer, null, locale)).toBe(
      `Last 8h: ${totals.runner} by runners, ${totals.person} by a person, 0 by the board, ${totals.total} in all, against 31 in the 8h before`,
    );
  });

  it('gives Moved\'s three sources on a marked bar, opening on its time', () => {
    const answer = withBar(sampleHealth(), 4, { runner: 2, person: 1, board: 0, moved: 3 });

    expect(markedWords('moved', answer, 4, locale)).toBe(
      `${momentLabel(answer.bars[4].start, 20, locale)}: 2 by runners, 1 by a person, 0 by the board`,
    );
  });

  it('gives In flight\'s three counts, at rest from the last bar', () => {
    const answer = sampleHealth();
    const last = answer.bars[BARS - 1];

    expect(markedWords('inFlight', answer, null, locale)).toBe(
      `Now: ${last.inFlight} in flight, ${last.waiting} waiting, ${last.done} done`,
    );
    expect(markedWords('inFlight', answer, 3, locale)).toContain(
      `${answer.bars[3].inFlight} in flight, ${answer.bars[3].waiting} waiting, ${answer.bars[3].done} done`,
    );
  });

  it('gives the age of the oldest and the split by age, in the tile\'s words', () => {
    const answer = sampleHealth();
    const last = answer.bars[BARS - 1];

    expect(markedWords('oldest', answer, 5, locale)).toContain('the oldest has been in flight 1h, ');
    expect(markedWords('oldest', answer, 5, locale)).toContain(splitSentence(answer.bars[5].ages));
    expect(markedWords('oldest', answer, null, locale)).toBe(
      `Now: the oldest has been in flight ${formatSpan(last.oldestInFlightSeconds!)}, ${splitSentence(last.ages)}`,
    );
    expect(markedWords('oldest', answer, null, locale)).toMatch(/2 past normal · 1 aging · 2 healthy$/);
  });

  it('adds issues in a column with no normal yet to the split', () => {
    const answer = withBar(sampleHealth(), 5, { ages: { pastNormal: 1, aging: 0, healthy: 2, noNormal: 3 } });

    expect(markedWords('oldest', answer, 5, locale)).toMatch(/1 past normal · 0 aging · 2 healthy · 3 no normal yet$/);
  });

  it('says nothing in flight, with no split, when the oldest is null', () => {
    const answer = sampleHealth();

    const none = markedWords('oldest', withBar(answer, 5, { oldestInFlightSeconds: null }), 5, locale);

    expect(none).toContain('nothing in flight');
    expect(none).not.toContain('past normal');
  });

  it('gives pull requests and questions apart, and how long the oldest waited', () => {
    const answer = withBar(sampleHealth(), 6, { waitingPullRequests: 2, waitingQuestions: 1, oldestWaitSeconds: 7800 });

    expect(markedWords('waiting', answer, 6, locale)).toBe(
      `${momentLabel(answer.bars[6].start, 20, locale)}: 2 pull requests and 1 question waiting, oldest waited 2h 10m`,
    );
  });

  it('says nothing waiting, and an unknown wait, in words', () => {
    const answer = sampleHealth();

    expect(markedWords('waiting', withBar(answer, 6, { waitingPullRequests: 0, waitingQuestions: 0 }), 6, locale)).toContain(
      'nothing waiting',
    );
    expect(
      markedWords('waiting', withBar(answer, 6, { waitingPullRequests: 1, waitingQuestions: 0, oldestWaitSeconds: null }), 6, locale),
    ).toContain('oldest wait unknown');
  });

  it('reads the last bar at rest on the other three, and as rest for an index with no bar', () => {
    const answer = sampleHealth();

    for (const tile of ['inFlight', 'oldest', 'waiting'] as Tile[]) {
      expect(markedWords(tile, answer, null, locale).startsWith('Now: ')).toBe(true);
      expect(markedWords(tile, answer, 99, locale)).toBe(markedWords(tile, answer, null, locale));
    }
    expect(markedWords('moved', answer, 99, locale)).toBe(markedWords('moved', answer, null, locale));
  });

  it('says nothing for an answer with no bars, and for Runners', () => {
    for (const tile of TILES) expect(markedWords(tile, sampleHealth({ bars: [] }), null)).toBe('');
    expect(markedWords('runners', sampleHealth(), null)).toBe('');
  });
});
