import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { flowHref, readout, tileLabel } from '../lib/boardHealth';
import type { Tile } from '../lib/boardHealth';
import { sampleBar, sampleHealth } from '../lib/boardHealthSample';
import type { BoardHealth } from '../types';
import { InFlightTile, MovedTile, OldestTile, WaitingTile } from './BoardHealthTiles';

const escape = (text: string): string => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** All four tiles over one answer, as the strip draws them. */
function tiles(answer: BoardHealth, index: number | null, href = '/reports/cumulative-flow') {
  const readouts = readout(answer, index);
  const props = { answer, index, onIndex: () => {} };

  return renderToStaticMarkup(
    <MemoryRouter>
      <MovedTile {...props} readout={readouts.moved} />
      <InFlightTile {...props} readout={readouts.inFlight} href={href} />
      <OldestTile {...props} readout={readouts.oldest} />
      <WaitingTile {...props} readout={readouts.waiting} />
    </MemoryRouter>,
  );
}

describe('the four health tiles', () => {
  it('draws four tiles', () => {
    const html = tiles(sampleHealth(), null);

    expect(html.match(/<section /g)).toHaveLength(4);
    for (const tile of ['moved', 'inFlight', 'oldest', 'waiting']) expect(html).toContain(`data-tile="${tile}"`);
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

    expect(html.match(/data-part="value"/g)).toHaveLength(4);
    expect(html.match(/data-part="second"/g)).toHaveLength(4);
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
      })),
    };

    expect(tiles(quiet, null)).toContain('nothing waiting');
  });
});
