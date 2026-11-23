import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { BARS, flowHref } from '../lib/boardHealth';
import type { HealthWindow } from '../lib/boardHealth';
import { dialogTitle, legend, markedWords, movedSummary } from '../lib/boardHealthDialog';
import type { DialogTile } from '../lib/boardHealthDialog';
import { sampleHealth } from '../lib/boardHealthSample';
import type { BoardHealth } from '../types';
import { BoardHealthDialog } from './BoardHealthDialog';

const escape = (text: string): string => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

const TILES: DialogTile[] = ['moved', 'inFlight', 'oldest', 'waiting'];

/** One open dialog over `answer`, as the strip mounts it. */
function dialog(tile: DialogTile, answer: BoardHealth, index: number | null = null, window: HealthWindow = '8h', href?: string) {
  return renderToStaticMarkup(
    <MemoryRouter>
      <BoardHealthDialog
        tile={tile}
        answer={answer}
        index={index}
        onIndex={() => {}}
        window={window}
        onWindow={() => {}}
        onClose={() => {}}
        href={href}
      />
    </MemoryRouter>,
  );
}

describe('the health dialogs', () => {
  it.each(TILES)('opens the %s dialog as a modal, titled with the tile and the window', (tile) => {
    const answer = sampleHealth();
    const html = dialog(tile, answer);

    expect(html).toContain('role="dialog"');
    expect(html).toContain('aria-modal="true"');
    expect(html).toContain(`>${escape(dialogTitle(tile, answer))}</h3>`);
    expect(html).toContain(`data-dialog="${tile}"`);
  });

  it('marks nothing as a tile, so the strip\'s own tile selectors and counts leave it alone', () => {
    for (const tile of TILES) {
      const html = dialog(tile, sampleHealth(), 3);

      expect(html).not.toContain('data-tile');
      expect(html).not.toContain('hatch-health-moment');
    }
  });

  it.each(TILES)('puts the window control in the %s dialog, the answer\'s own choice pressed', (tile) => {
    const html = dialog(tile, sampleHealth(), null, '12h');
    const buttons = html.match(/<button[^>]*hatch-health-window[^>]*>[^<]*<\/button>/g) ?? [];

    expect(buttons).toHaveLength(5);
    expect(buttons.find((b) => b.includes('aria-pressed="true"'))).toContain('>12h<');
  });

  it.each(TILES)('names every mark of the %s chart in its legend', (tile) => {
    const html = dialog(tile, sampleHealth());

    for (const mark of legend(tile)) {
      expect(html).toContain(`data-mark="${mark.key}"`);
      expect(html).toContain(escape(mark.label));
    }
  });

  it('draws the scale\'s top at the side and the times along the bottom', () => {
    const html = dialog('moved', sampleHealth());

    expect(html).toContain('data-part="scale-top">4<');
    expect(html.match(/class="hatch-health-dialog-tick[ "]/g)).toHaveLength(5);
    expect(html).toContain('hatch-health-dialog-tick--end');
  });

  it('draws the chart at the size it measures, and never stretched', () => {
    const html = dialog('moved', sampleHealth());

    expect(html).toContain('viewBox="0 0 480 200"');
    expect(html).not.toContain('preserveAspectRatio');
  });

  it('draws the marked line only when a bar is marked', () => {
    expect(dialog('oldest', sampleHealth())).not.toContain('hatch-health-dialog-moment');
    expect(dialog('oldest', sampleHealth(), 3).match(/hatch-health-dialog-moment/g)).toHaveLength(1);
  });

  it.each(TILES)('says the %s dialog\'s marked bar in words, and the window\'s at rest', (tile) => {
    const answer = sampleHealth();

    for (const index of [null, 5]) {
      expect(dialog(tile, answer, index)).toContain(`data-part="words" aria-live="polite">${escape(markedWords(tile, answer, index))}<`);
    }
  });

  it('makes the chart a focusable group that says how to mark a moment', () => {
    const html = dialog('waiting', sampleHealth());

    expect(html).toMatch(/data-part="chart" tabindex="0" role="group" aria-label="Waiting on you, last 8h\. Arrow keys mark a moment\."/);
  });
});

describe('the Moved dialog', () => {
  it('stacks 24 bars and tells the three sources apart by pattern as well as shade', () => {
    const html = dialog('moved', sampleHealth());

    expect(html.match(/data-bar="/g)).toHaveLength(BARS);
    expect(html.match(/<pattern /g)?.length).toBeGreaterThanOrEqual(3);
    for (const source of ['runner', 'person']) expect(html).toContain(`hatch-health-dialog-seg--${source}`);
    expect(html).toContain('patternTransform="rotate(45)"');
    expect(html).toContain('<circle');
  });

  it('points every segment\'s fill at a pattern the chart defines', () => {
    const html = dialog('moved', sampleHealth());
    const used = [...html.matchAll(/fill="url\(#([^)]+)\)"/g)].map((m) => m[1]);
    const defined = [...html.matchAll(/<pattern id="([^"]+)"/g)].map((m) => m[1]);

    expect(used.length).toBeGreaterThan(0);
    for (const id of used) expect(defined).toContain(id);
  });

  it('gives each source\'s total, the window\'s, and the window before', () => {
    const answer = sampleHealth({ previousTotal: 31 });
    const html = dialog('moved', answer);

    for (const row of movedSummary(answer)) {
      expect(html).toContain(`data-row="${row.key}"`);
      expect(html).toContain(`<dt>${escape(row.label)}</dt><dd>${row.value}</dd>`);
    }
  });
});

describe('the In flight dialog', () => {
  it('draws the three bands, 24 points along each edge', () => {
    const html = dialog('inFlight', sampleHealth());

    for (const band of ['done', 'inflight', 'waiting']) {
      const points = new RegExp(`hatch-health-band--${band}" points="([^"]*)"`).exec(html)?.[1] ?? '';

      expect(points.split(' ')).toHaveLength(BARS * 2);
    }
  });

  it('gives each band\'s count at the marked bar, and at rest from the last', () => {
    const answer = sampleHealth();

    expect(dialog('inFlight', answer, 5)).toContain('<dt>Done</dt><dd>5</dd>');
    expect(dialog('inFlight', answer)).toContain(`<dt>Done</dt><dd>${BARS - 1}</dd>`);
  });

  it('links to the cumulative flow report, on the tile\'s own address', () => {
    const answer = sampleHealth();
    const href = flowHref(answer, ['AER']);
    const html = dialog('inFlight', answer, null, '8h', href);

    expect(html).toContain(`href="${escape(href)}"`);
    expect(html).toContain('Open the cumulative flow report');
  });

  it('draws no link when it is given no address', () => {
    expect(dialog('inFlight', sampleHealth())).not.toContain('Open the cumulative flow report');
  });
});

describe('the Oldest in flight dialog', () => {
  it('draws the line across 24 bars and a dot for today', () => {
    const html = dialog('oldest', sampleHealth());
    const path = /hatch-health-dialog-line" d="([^"]*)"/.exec(html)?.[1] ?? '';

    expect(path.match(/[ML] /g)).toHaveLength(BARS);
    expect(html).toContain('hatch-health-dialog-dot');
  });
});

describe('the Waiting on you dialog', () => {
  it('draws pull requests and questions as two areas told apart by pattern', () => {
    const html = dialog('waiting', sampleHealth());

    expect(html).toContain('hatch-health-dialog-seg--pullRequests');
    expect(html).toContain('hatch-health-dialog-seg--questions');
    // Two in the chart, and the legend's swatches draw the same two.
    expect(html.match(/<pattern id="hd-[^"-]+-(?:pullRequests|questions)"/g)).toHaveLength(2);
    expect(html.match(/<pattern id="[^"]*-swatch-/g)).toHaveLength(2);
    expect(html.match(/hatch-health-dialog-edge/g)).toHaveLength(2);
  });

  it('says how long the oldest has waited, from the last bar', () => {
    const answer = sampleHealth();
    const bars = answer.bars.map((bar, i) =>
      i === BARS - 1 ? { ...bar, waitingPullRequests: 1, waitingQuestions: 0, oldestWaitSeconds: 7800 } : bar,
    );

    expect(dialog('waiting', { ...answer, bars })).toContain('Now: oldest waited 2h 10m');
  });
});
