import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { flowHref, readout, tileLabel } from '../lib/boardHealth';
import type { Tile } from '../lib/boardHealth';
import { LANE_LEGEND, MAX_LANES, cellWords, drawnLanes, hoverList } from '../lib/boardHealthLanes';
import { sampleBar, sampleHealth, sampleRunners } from '../lib/boardHealthSample';
import type { BoardHealth, BoardHealthLane } from '../types';
import { InFlightTile, MovedTile, OldestTile, RunnersTile, WaitingTile } from './BoardHealthTiles';

const escape = (text: string): string => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** All five tiles over one answer, as the strip draws them. */
function tiles(answer: BoardHealth, index: number | null, href = '/reports/cumulative-flow') {
  const readouts = readout(answer, index);
  const props = { answer, index, onIndex: () => {} };
  const open = { onOpen: () => {} };

  return renderToStaticMarkup(
    <MemoryRouter>
      <MovedTile {...props} {...open} readout={readouts.moved} />
      <InFlightTile {...props} {...open} readout={readouts.inFlight} href={href} />
      <OldestTile {...props} {...open} readout={readouts.oldest} onOpenPast={() => {}} />
      <WaitingTile {...props} {...open} readout={readouts.waiting} />
      <RunnersTile {...props} {...open} readout={readouts.runners} />
    </MemoryRouter>,
  );
}

/** The Oldest tile alone, as the strip draws it. */
function oldestTile(answer: BoardHealth, index: number | null): string {
  const readouts = readout(answer, index);

  return renderToStaticMarkup(
    <MemoryRouter>
      <OldestTile answer={answer} index={index} onIndex={() => {}} readout={readouts.oldest} onOpenPast={() => {}} />
    </MemoryRouter>,
  );
}

describe('the Oldest in flight tile', () => {
  it('draws the age bands and no line', () => {
    const html = oldestTile(sampleHealth(), null);

    for (const band of ['healthy', 'aging', 'past']) {
      expect(html).toMatch(new RegExp(`<polygon class="hatch-health-band hatch-health-band--${band}" points="[^"]+"`));
    }
    expect(html).not.toContain('hatch-health-line');
    expect(html).not.toContain('hatch-health-dot');
  });

  it('draws the neutral band only when some column has no normal yet', () => {
    expect(oldestTile(sampleHealth(), null)).not.toContain('hatch-health-band--nonormal');

    const bars = sampleHealth().bars.map((bar) => ({ ...bar, ages: { ...bar.ages, noNormal: 1 } }));

    expect(oldestTile(sampleHealth({ bars }), null)).toContain('hatch-health-band--nonormal');
  });

  it('links the detail line to the oldest issue at rest, and shows the peek', () => {
    const html = oldestTile(sampleHealth(), null);

    expect(html).toContain('href="/issues/HA-101"');
    expect(html).toContain('HA-101 · In Progress');
    expect(html).toContain('role="tooltip"');
    expect(html).toContain('HA-101 The oldest issue in flight');
    expect(html).toContain('Stood 4h in In Progress');
    expect(html).toContain('normal 2h');
    expect(html).toMatch(/class="hatch-health-value" tabindex="0" aria-describedby=/);
  });

  it('has no link and no peek on a marked bar, and says the split instead', () => {
    const html = oldestTile(sampleHealth(), 4);

    expect(html).not.toContain('/issues/');
    expect(html).not.toContain('role="tooltip"');
    expect(html).not.toContain('tabindex="0" aria-describedby');
    expect(html).toContain(escape(readout(sampleHealth(), 4).oldest.detail));
  });

  it('has no link and no peek with no issue in flight', () => {
    const html = oldestTile(sampleHealth({ inFlightIssues: [] }), null);

    expect(html).not.toContain('/issues/');
    expect(html).not.toContain('role="tooltip"');
  });
});

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
    expect(html).not.toContain('hatch-health-spark--link');
  });

  it('says all five tiles open a dialog when pressed', () => {
    const html = tiles(sampleHealth(), null);
    const sparks = html.split('<section ').slice(1).map((tile) => tile.includes('hatch-health-spark--open'));

    // moved, inFlight, oldest, waiting, runners - in the order they are drawn.
    expect(sparks).toEqual([true, true, true, true, true]);
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

  it('draws no names, but every cell says whose it is', () => {
    const html = runnersTile(sampleHealth(), null);

    expect(html).not.toContain('hatch-health-lane-name');
    for (const lane of drawnLanes(sampleHealth()).lanes) {
      for (const cell of lane.cells) expect(html).toContain(`title="${lane.name}: ${cellWords(cell)}"`);
    }
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

describe('the Waiting on you tile when clear', () => {
  const clearHealth = (over: Partial<BoardHealth> = {}): BoardHealth => {
    const base = sampleHealth();

    return {
      ...base,
      bars: base.bars.map((bar) => ({ ...bar, waitingPullRequests: 0, waitingQuestions: 0, waiting: 0, oldestWaitSeconds: null })),
      lastWaitedAt: new Date(Date.parse(base.to) - (3 * 3600 + 20 * 60) * 1000).toISOString(),
      ...over,
    };
  };

  const waitingTile = (answer: BoardHealth, index: number | null): string => {
    const readouts = readout(answer, index);

    return renderToStaticMarkup(
      <MemoryRouter>
        <WaitingTile answer={answer} index={index} onIndex={() => {}} onOpen={() => {}} readout={readouts.waiting} />
      </MemoryRouter>,
    );
  };

  it('takes the good tone, with a check mark said as clear, the time and the moves without a person', () => {
    const html = waitingTile(clearHealth(), null);

    expect(html).toContain('hatch-health-tile--good');
    expect(html).toMatch(/aria-label="clear"[^>]*>✓</);
    expect(html).toContain('clear for 3h 20m');
    expect(html).toMatch(/\d+ moves? without you/);
  });

  it('draws bars in the good colour in place of the flat line', () => {
    const html = waitingTile(clearHealth(), null);

    expect(html).toContain('hatch-health-bar hatch-health-bar--clear');
    expect(html).not.toContain('hatch-health-line');
    expect(html).not.toContain('hatch-health-area');
  });

  it('marks a bar by its own count', () => {
    expect(waitingTile(clearHealth(), 4)).toMatch(/\d+ moves? without you/);
  });

  it('draws the steps, and no bars, when some bar had something waiting', () => {
    const base = clearHealth();
    const bars = base.bars.map((bar, i) => (i === 3 ? { ...bar, waitingQuestions: 1 } : bar));
    const html = waitingTile({ ...base, bars }, null);

    expect(html).toContain('hatch-health-line');
    expect(html).not.toContain('hatch-health-bar--clear');
    expect(html).toContain('hatch-health-tile--good');
  });

  it('stays neutral, with the steps, while something waits', () => {
    const html = waitingTile(sampleHealth(), null);

    expect(html).not.toContain('hatch-health-tile--good');
    expect(html).not.toContain('✓');
    expect(html).toContain('hatch-health-line');
  });

  it('falls back to nothing waiting when the read has no clear time', () => {
    const html = waitingTile(clearHealth({ lastWaitedAt: null }), null);

    expect(html).toContain('nothing waiting');
    expect(html).not.toContain('✓');
  });
});

describe('the Oldest in flight warning', () => {
  const past = (key: string): BoardHealth['inFlightIssues'][number] => ({
    ...sampleHealth().inFlightIssues[0],
    key,
    band: 'pastNormal',
  });
  const aging = { ...sampleHealth().inFlightIssues[2] };

  /** A tile with no `onOpenPast`: the sign in its readout, and no way to lead. */
  const bare = (answer: BoardHealth): string => {
    const readouts = readout(answer, null);

    return renderToStaticMarkup(
      <MemoryRouter>
        <OldestTile answer={answer} index={null} onIndex={() => {}} readout={readouts.oldest} />
      </MemoryRouter>,
    );
  };

  it('is a pair of buttons with several past normal, each saying where it leads', () => {
    const html = oldestTile(sampleHealth(), null);
    const buttons = html.match(/<button[^>]*aria-label="Open the 2 issues past normal"[^>]*>/g) ?? [];

    expect(buttons).toHaveLength(2);
    expect(html).toContain('hatch-health-glyph-lead');
    expect(html).toMatch(/<button[^>]*class="hatch-health-lead hatch-health-second-lead"[^>]*>2 past normal<\/button>/);
  });

  it('is a link to the issue when exactly one is past normal', () => {
    const answer = sampleHealth({ pastNormal: 1, inFlightIssues: [past('HA-7'), aging] });
    const html = oldestTile(answer, null);
    const links = html.match(/<a[^>]*aria-label="Open HA-7, the issue past normal"[^>]*>/g) ?? [];

    expect(links).toHaveLength(2);
    for (const link of links) expect(link).toContain('href="/issues/HA-7"');
    expect(html).not.toMatch(/<button[^>]*hatch-health-lead/);
  });

  it('is plain text with none past normal', () => {
    const answer = sampleHealth({ pastNormal: 0, inFlightIssues: [aging] });
    const html = oldestTile(answer, null);

    expect(html).toContain('none past normal');
    expect(html).not.toContain('hatch-health-lead');
    expect(html).not.toContain('⚠');
  });

  it('is plain on a marked bar, where the line is a time', () => {
    expect(oldestTile(sampleHealth(), 5)).not.toContain('hatch-health-lead');
  });

  it('never draws a warning sign outside a control, in any of the five tiles', () => {
    const several = tiles(sampleHealth(), null);
    const one = tiles(sampleHealth({ pastNormal: 1, inFlightIssues: [past('HA-7')] }), null);

    for (const html of [several, one]) {
      const without = html.replace(/<(a|button)\b[^>]*>[\s\S]*?<\/\1>/g, '');
      expect(without).not.toContain('⚠');
    }
  });

  it('draws no warning sign at all when given the sign and no way to lead', () => {
    expect(bare(sampleHealth())).not.toContain('⚠');
  });
});
