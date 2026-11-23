import { describe, expect, it } from 'vitest';
import {
  LANE_LEGEND,
  MAX_LANES,
  cellWords,
  drawnLanes,
  hoverList,
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
