import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { CumulativeFlowBody } from './CumulativeFlowPage';
import type { CumulativeFlow, Status } from '../types';

/* No DOM runner, so `useLoaded` is not driven: the body is rendered from
   props, the way TimelinePage.test.tsx renders TimelineBody. */
const NOW = new Date('2026-10-06T00:00:00Z');
const EMPTY = 'Nothing was on the board in this range.';
const FOOTNOTE = 'hatch-timeline-footnote';
const UNPINNED = 'Click the chart to pin a moment.';

const statuses = [{ id: 1, name: 'To Do', color: '#3366cc' }] as Status[];

const answer = (over: Partial<CumulativeFlow> = {}): CumulativeFlow => ({
  from: '2026-10-01T00:00:00.000Z',
  to: '2026-10-11T00:00:00.000Z',
  step: 'day',
  columns: [1],
  points: [{ at: '2026-10-02T00:00:00.000Z', counts: [4] }],
  unresolved: 0,
  ...over,
});

const render = (data: CumulativeFlow): string =>
  renderToStaticMarkup(
    <CumulativeFlowBody answer={data} statuses={statuses} offsetMinutes={0} now={NOW} />,
  );

describe('CumulativeFlowBody', () => {
  it('draws the chart when there is work on the board, with no footnote at zero', () => {
    const html = render(answer());

    expect(html).toContain('class="hatch-cumulative-flow"');
    expect(html).toContain(UNPINNED);
    expect(html).not.toContain(EMPTY);
    expect(html).not.toContain(FOOTNOTE);
  });

  it('says there is nothing to show when there are no points', () => {
    const html = render(answer({ points: [] }));

    expect(html).toContain(EMPTY);
    expect(html).not.toContain('hatch-cumulative-flow"');
  });

  it('says there is nothing to show when every count everywhere is zero', () => {
    const html = render(answer({ points: [{ at: '2026-10-02T00:00:00.000Z', counts: [0] }] }));

    expect(html).toContain(EMPTY);
    expect(html).not.toContain('hatch-cumulative-flow"');
  });

  it('footnotes one unplaced column change in the singular', () => {
    const html = render(answer({ unresolved: 1 }));

    expect(html).toContain(FOOTNOTE);
    expect(html).toContain('1 column change could not be placed');
    expect(html).not.toContain('1 column changes');
  });

  it('footnotes several in the plural', () => {
    const html = render(answer({ unresolved: 3 }));

    expect(html).toContain('3 column changes could not be placed');
  });

  it('carries no footnote when nothing is unresolved', () => {
    const html = render(answer({ unresolved: 0 }));

    expect(html).not.toContain(FOOTNOTE);
  });

  it('carries the footnote on the empty state too', () => {
    const html = render(answer({ points: [], unresolved: 2 }));

    expect(html).toContain(EMPTY);
    expect(html).toContain('2 column changes could not be placed');
  });
});
