import { describe, expect, it } from 'vitest';
import {
  LANE_LEGEND,
  MAX_LANES,
  allLanes,
  cellWords,
  drawnLanes,
  hoverList,
  laneStanding,
  lanesMarkedWords,
  lastHeardWords,
  runnersTileWords,
} from './boardHealthLanes';
import { momentLabel } from './cumulativeFlow';
import { sampleHealth, sampleRunners } from './boardHealthSample';
import type { BoardHealthCell, BoardHealthLane } from '../types';

const lane = (name: string, over: Partial<BoardHealthLane> = {}): BoardHealthLane => ({
  name,
  up: true,
  paused: false,
  lastSeenAt: '2026-11-22T15:40:00.000Z',
  cells: Array.from({ length: 24 }, () => ({ state: 'idle', why: null })),
  ...over,
});

describe('drawnLanes', () => {
  it('caps at eight and counts the rest as omitted', () => {
    const lanes = Array.from({ length: 11 }, (_, i) => lane(`r${i}`));
    const drawn = drawnLanes(sampleHealth({ runners: { up: 11, paused: 0, withLane: 11, lanes } }));

    expect(drawn.lanes).toHaveLength(MAX_LANES);
    expect(drawn.omitted).toBe(3);
  });

  it('omits nothing at or under the cap', () => {
    const lanes = Array.from({ length: 3 }, (_, i) => lane(`r${i}`));
    const drawn = drawnLanes(sampleHealth({ runners: { up: 3, paused: 0, withLane: 3, lanes } }));

    expect(drawn.lanes).toHaveLength(3);
    expect(drawn.omitted).toBe(0);
  });
});

describe('cellWords', () => {
  const words = (cell: BoardHealthCell): string => cellWords(cell);

  it('says each state plainly, apart from paused and the two that carry a reason', () => {
    expect(words({ state: 'moved', why: null })).toBe('moved a ticket');
    expect(words({ state: 'working', why: null })).toBe('working');
    expect(words({ state: 'idle', why: null })).toBe('idle');
    expect(words({ state: 'off', why: null })).toBe('off');
    expect(words({ state: 'blank', why: null })).toBe('no record yet');
  });

  it('says a pause by a person plainly, and a board pause by name', () => {
    expect(words({ state: 'paused', why: null })).toBe('paused');
    expect(words({ state: 'paused', why: 'board' })).toBe('paused by the board');
  });

  it('says a let-go by its own reason', () => {
    expect(words({ state: 'let-go', why: 'dropped' })).toBe('dropped a ticket');
    expect(words({ state: 'let-go', why: 'lapsed' })).toBe('lapsed');
    expect(words({ state: 'let-go', why: 'stalled' })).toBe('stalled');
    expect(words({ state: 'let-go', why: 'errored' })).toBe('errored');
  });

  it("says down in lib/runners.ts' own words, lower-cased, and agent down for the rest", () => {
    expect(words({ state: 'down', why: 'out-of-usage' })).toBe('out of usage');
    expect(words({ state: 'down', why: 'not-starting' })).toBe('agent not starting');
    expect(words({ state: 'down', why: 'signed-out' })).toBe('agent needs signing in');
    expect(words({ state: 'down', why: 'agent-down' })).toBe('agent down');
    expect(words({ state: 'down', why: null })).toBe('agent down');
  });
});

describe('runnersTileWords', () => {
  it('reads "N of M" over "up · P paused" at rest, with lanes paused', () => {
    const answer = sampleHealth({ runners: { up: 3, paused: 1, withLane: 4, lanes: [] } });

    expect(runnersTileWords(answer, null)).toEqual({ value: '3 of 4', second: 'up · 1 paused', detail: '' });
  });

  it('reads "up" alone at rest when none is paused', () => {
    const answer = sampleHealth({ runners: { up: 4, paused: 0, withLane: 4, lanes: [] } });

    expect(runnersTileWords(answer, null).second).toBe('up');
  });

  it('reads "0 of 0" over "none up" with no lanes at all', () => {
    const answer = sampleHealth({ runners: { up: 0, paused: 0, withLane: 0, lanes: [] } });

    expect(runnersTileWords(answer, null)).toEqual({ value: '0 of 0', second: 'none up', detail: '' });
  });

  it('says how many lanes are not drawn, at rest only when some are capped', () => {
    const lanes = Array.from({ length: 10 }, (_, i) => lane(`r${i}`));
    const answer = sampleHealth({ runners: { up: 10, paused: 0, withLane: 10, lanes } });

    expect(runnersTileWords(answer, null).detail).toBe('2 not drawn');
  });

  it('on a bar, counts lanes neither off nor blank, of all lanes, and names the moment', () => {
    const lanes = [
      lane('a', { cells: Array.from({ length: 24 }, (_, i) => (i === 5 ? { state: 'working', why: null } : { state: 'off', why: null }) as BoardHealthCell) }),
      lane('b', { cells: Array.from({ length: 24 }, (_, i) => (i === 5 ? { state: 'blank', why: null } : { state: 'idle', why: null }) as BoardHealthCell) }),
      lane('c', { cells: Array.from({ length: 24 }, () => ({ state: 'idle', why: null }) as BoardHealthCell) }),
    ];
    const answer = sampleHealth({ runners: { up: 3, paused: 0, withLane: 3, lanes } });

    const words = runnersTileWords(answer, 5);
    expect(words.value).toBe('2 of 3');
    expect(words.second).toBe(momentLabel(answer.bars[5].start, answer.barMinutes));
  });

  it('says how many lanes are paused on a bar, as the detail', () => {
    const lanes = [
      lane('a', { cells: Array.from({ length: 24 }, () => ({ state: 'paused', why: null }) as BoardHealthCell) }),
      lane('b', { cells: Array.from({ length: 24 }, () => ({ state: 'paused', why: null }) as BoardHealthCell) }),
      lane('c', { cells: Array.from({ length: 24 }, () => ({ state: 'idle', why: null }) as BoardHealthCell) }),
    ];
    const answer = sampleHealth({ runners: { up: 3, paused: 2, withLane: 3, lanes } });

    expect(runnersTileWords(answer, 0).detail).toBe('2 paused');
  });
});

describe('hoverList', () => {
  it('names each drawn runner and its words at the bar, then how many are left out', () => {
    const lanes = Array.from({ length: 9 }, (_, i) =>
      lane(`r${i}`, { cells: Array.from({ length: 24 }, (_, j) => (j === 2 ? { state: 'working', why: null } : { state: 'idle', why: null }) as BoardHealthCell) }),
    );
    const answer = sampleHealth({ runners: { up: 9, paused: 0, withLane: 9, lanes } });

    const list = hoverList(answer, 2);
    expect(list.lines).toHaveLength(MAX_LANES);
    expect(list.lines[0]).toEqual({ name: 'r0', words: 'working' });
    expect(list.omitted).toBe(1);
  });
});

describe('allLanes', () => {
  it('answers every lane where drawnLanes caps at eight', () => {
    const lanes = Array.from({ length: 12 }, (_, i) => lane(`r${i}`));
    const answer = sampleHealth({ runners: { up: 12, paused: 0, withLane: 12, lanes } });

    expect(allLanes(answer)).toHaveLength(12);
    expect(allLanes(answer).map((l) => l.name)).toEqual(lanes.map((l) => l.name));
    expect(drawnLanes(answer).lanes).toHaveLength(MAX_LANES);
  });
});

describe('laneStanding', () => {
  it('says paused, up or not up', () => {
    expect(laneStanding(lane('a'))).toBe('up');
    expect(laneStanding(lane('a', { paused: true }))).toBe('paused');
    expect(laneStanding(lane('a', { up: false }))).toBe('not up');
  });

  it('puts paused ahead of not up, as the read marks a paused runner', () => {
    expect(laneStanding(lane('a', { up: false, paused: true }))).toBe('paused');
  });
});

describe('lastHeardWords', () => {
  const answer = sampleHealth();
  const time = new Date(answer.to).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  const before = (minutes: number): string => new Date(Date.parse(answer.to) - minutes * 60_000).toISOString();

  it("says 'at' the window's end for a runner heard from just now", () => {
    expect(lastHeardWords(lane('a', { lastSeenAt: answer.to }), answer)).toBe(`last heard at ${time}`);
    expect(lastHeardWords(lane('a', { lastSeenAt: before(0.5) }), answer)).toBe(`last heard at ${time}`);
  });

  it("says 'at' for a last seen after the window's end, rather than a negative span", () => {
    expect(lastHeardWords(lane('a', { lastSeenAt: before(-5) }), answer)).toBe(`last heard at ${time}`);
  });

  it('says how long before the end for a runner heard from minutes earlier', () => {
    expect(lastHeardWords(lane('a', { lastSeenAt: before(190) }), answer)).toBe(`last heard 3h 10m before ${time}`);
    expect(lastHeardWords(lane('a', { lastSeenAt: before(7) }), answer)).toBe(`last heard 7m before ${time}`);
  });

  it('says how long before for a runner last seen before the window began', () => {
    expect(lastHeardWords(lane('a', { lastSeenAt: before(26 * 60) }), answer)).toBe(`last heard 1d 2h before ${time}`);
  });
});

describe('lanesMarkedWords', () => {
  const answer = sampleHealth();
  const locale = 'en-GB';

  it('counts the runners at rest', () => {
    const { up, paused, withLane } = answer.runners;

    expect(lanesMarkedWords(answer, null)).toBe(`${withLane} runners, ${up} up, ${paused} paused`);
  });

  it('says "1 runner" for one', () => {
    const one = sampleHealth({ runners: { up: 1, paused: 0, withLane: 1, lanes: [lane('solo')] } });

    expect(lanesMarkedWords(one, null)).toBe('1 runner, 1 up, 0 paused');
  });

  it("opens on the bar's time and names every runner at it, with none left as not drawn", () => {
    const lanes = Array.from({ length: 12 }, (_, i) =>
      lane(`r${i}`, { cells: Array.from({ length: 24 }, () => ({ state: i % 2 ? 'paused' : 'working', why: null }) as BoardHealthCell) }),
    );
    const twelve = sampleHealth({ runners: { up: 12, paused: 6, withLane: 12, lanes } });
    const words = lanesMarkedWords(twelve, 4, locale);

    expect(words.startsWith(`${momentLabel(twelve.bars[4].start, twelve.barMinutes, locale)}: r0 working, r1 paused, `)).toBe(true);
    for (const l of lanes) expect(words).toContain(l.name);
    expect(words).not.toContain('not drawn');
  });

  it("uses each cell's own words, why included", () => {
    const marked = lanesMarkedWords(answer, 0, locale);

    for (const l of answer.runners.lanes) expect(marked).toContain(`${l.name} ${cellWords(l.cells[0])}`);
  });
});

describe('LANE_LEGEND', () => {
  it('has the mock-up\'s seven lines', () => {
    expect(LANE_LEGEND).toHaveLength(7);
    expect(LANE_LEGEND.map((line) => line.title)).toEqual([
      'Moved a ticket',
      'Working',
      'Let go or errored',
      'Out of usage or agent down',
      'Idle',
      'Paused',
      'Off',
    ]);
  });
});

// The sample fixture covers every state at least once, so a component test
// can draw every cell kind without hand-building a lane.
describe('sampleRunners', () => {
  it('covers every cell state', () => {
    const runners = sampleRunners();
    const states = new Set(runners.lanes.flatMap((l) => l.cells.map((c) => c.state)));
    expect(states).toEqual(new Set(['moved', 'working', 'idle', 'off', 'paused', 'down', 'let-go', 'blank']));
  });
});
