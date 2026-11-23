import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { flowHref, readout, tileLabel } from '../lib/boardHealth';
import type { Tile } from '../lib/boardHealth';
import { LANE_LEGEND, MAX_LANES, drawnLanes, hoverList } from '../lib/boardHealthLanes';
import { sampleBar, sampleHealth, sampleRunners } from '../lib/boardHealthSample';
import type { BoardHealth, BoardHealthLane } from '../types';
import { InFlightTile, MovedTile, OldestTile, RunnersTile, WaitingTile } from './BoardHealthTiles';

const escape = (text: string): string => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** All five tiles over one answer, as the strip draws them. */
function tiles(answer: BoardHealth, index: number | null, href = '/reports/cumulative-flow') {
  const readouts = readout(answer, index);
  const props = { answer, index, onIndex: () => {} };

  return renderToStaticMarkup(
    <MemoryRouter>
      <MovedTile {...props} readout={readouts.moved} />
      <InFlightTile {...props} readout={readouts.inFlight} href={href} />
      <OldestTile {...props} readout={readouts.oldest} />
      <WaitingTile {...props} readout={readouts.waiting} />
      <RunnersTile {...props} readout={readouts.runners} />
    </MemoryRouter>,
  );
}

describe('the five health tiles', () => {
  it('draws five tiles', () => {
    const html = tiles(sampleHealth(), null);

    expect(html.match(/<section /g)).toHaveLength(5);
    for (const tile of ['moved', 'inFlight', 'oldest', 'waiting', 'runners']) expect(html).toContain(`data-tile="${tile}"`);
  });

  it('gives each tile the sentence tileLabel writes for it, at rest and on a bar', () => {
    const answer = sampleHealth();

    for (const index of [null, 4]) {
      const readouts = readout(answer, index);
      const html = tiles(answer, index);

      for (const tile of ['moved', 'inFlight', 'oldest', 'waiting'] as Tile[]) {
        expect(html).toContain(`aria-label="${escape(tileLabel(tile, readouts[tile]))}"`);
      }
    }
  });

  it('colours each tile from the tone of its readout, and from nothing else', () => {
    const rising = tiles(sampleHealth(), null);
    expect(rising).toContain('hatch-health-tile--good'); // moved, up on the window before
    expect(rising).toContain('hatch-health-tile--warn'); // oldest, with cards past normal
    expect(rising).toContain('hatch-health-tile--neutral');

    const answer = sampleHealth();
    const falling = tiles({ ...answer, previousTotal: answer.total + 3, pastNormal: 0 }, null);
    expect(falling).toContain('hatch-health-tile--warn'); // moved, down
    expect(falling).not.toContain('hatch-health-tile--good');
  });

  it('says the moved arrow and the warning glyph in words as well as glyphs', () => {
    const html = tiles(sampleHealth(), null);

    expect(html).toContain('aria-label="up"');
    expect(html).toContain('aria-label="warning"');
  });

  it('puts a real link to the report on the In flight tile', () => {
    const answer = sampleHealth();
    const href = flowHref(answer, ['AER']);
    const html = tiles(answer, null, href);

    expect(html).toContain(`href="${escape(href)}"`);
    expect(html).toContain('hatch-health-spark--link');
  });

  it('draws the marked line on every tile only when a bar is marked', () => {
    const answer = sampleHealth();

    expect(tiles(answer, null)).not.toContain('hatch-health-moment');
    expect(tiles(answer, 3).match(/hatch-health-moment/g)).toHaveLength(4);
  });

  it('marks the same moment on all four tiles', () => {
    const lines = tiles(sampleHealth(), 3).match(/<line class="hatch-health-moment" x1="([^"]*)"/g) ?? [];

    expect(new Set(lines).size).toBe(1);
  });

  it('shows the hovered bar\'s own numbers in place of the window\'s', () => {
    const answer = sampleHealth();
    const html = tiles(answer, 4);

    expect(html).toContain(`>${sampleBar(4).moved}<`);
    expect(html).toContain('by runners');
  });

  it('puts the accent on the latest bar alone', () => {
    const html = tiles(sampleHealth(), null);

    expect(html.match(/hatch-health-bar--latest/g)).toHaveLength(1);
  });

  it('reads real text, not drawing, for every number', () => {
    const html = tiles(sampleHealth(), null);

    expect(html.match(/data-part="value"/g)).toHaveLength(5);
    expect(html.match(/data-part="second"/g)).toHaveLength(5);
  });

  it('says nothing waiting when nothing waits', () => {
    const answer = sampleHealth();
    const quiet = {
      ...answer,
      bars: answer.bars.map((bar) => ({
        ...bar,
        waitingPullRequests: 0,
        waitingQuestions: 0,
        oldestWaitSeconds: null,
        ages: { healthy: 0, aging: 0, pastNormal: 0, noNormal: 0 },
      })),
    };

    expect(tiles(quiet, null)).toContain('nothing waiting');
  });
});

function runnersTile(answer: BoardHealth, index: number | null) {
  const readouts = readout(answer, index);

  return renderToStaticMarkup(
    <MemoryRouter>
      <RunnersTile answer={answer} index={index} onIndex={() => {}} readout={readouts.runners} />
    </MemoryRouter>,
  );
}

const lane = (name: string, over: Partial<BoardHealthLane> = {}): BoardHealthLane => ({
  name,
  up: true,
  paused: false,
  lastSeenAt: '2026-11-22T15:40:00.000Z',
  cells: Array.from({ length: 24 }, () => ({ state: 'idle', why: null })),
  ...over,
});

describe('the Runners tile', () => {
  it('draws a lane row for every runner, up to the cap, and says how many are not drawn', () => {
    const lanes = Array.from({ length: 10 }, (_, i) => lane(`r${i}`));
    const answer = sampleHealth({ runners: { up: 10, paused: 0, withLane: 10, lanes } });

    const html = runnersTile(answer, null);
    expect(html.match(/hatch-health-lane-cells/g)).toHaveLength(MAX_LANES);
    expect(html).toContain('2 not drawn');
  });

  it('names each drawn runner', () => {
    const html = runnersTile(sampleHealth(), null);

    for (const lane of drawnLanes(sampleHealth()).lanes) expect(html).toContain(lane.name);
  });

  it('lists the hover words only while a bar is marked', () => {
    const rest = runnersTile(sampleHealth(), null);
    const onBar = runnersTile(sampleHealth(), 7);

    expect(rest).not.toContain('hatch-health-lane-hover');
    expect(onBar).toContain('hatch-health-lane-hover');
    const list = hoverList(sampleHealth(), 7);
    for (const line of list.lines) expect(onBar).toContain(line.words);
  });

  it('marks the moment with its own class, not the one the other four tiles count', () => {
    expect(runnersTile(sampleHealth(), null)).not.toContain('hatch-health-lane-moment');
    expect(runnersTile(sampleHealth(), 7)).toContain('hatch-health-lane-moment');
  });

  it('always carries the legend, the mock-up\'s seven lines, closed until pressed', () => {
    const html = runnersTile(sampleHealth(), null);

    expect(html).toContain('aria-expanded="false"');
    for (const line of LANE_LEGEND) expect(html).toContain(line.title);
    expect(LANE_LEGEND).toHaveLength(7);
  });

  it('reads "N of M" over "up · P paused" in its own value and second line', () => {
    const answer = sampleHealth({ runners: sampleRunners() });
    const html = runnersTile(answer, null);

    expect(html).toContain(`>${answer.runners.up} of ${answer.runners.withLane}<`);
  });
});
