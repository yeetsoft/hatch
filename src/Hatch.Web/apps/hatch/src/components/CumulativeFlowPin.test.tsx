import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { CumulativeFlowPin } from './CumulativeFlowPin';
import type { CumulativeFlowPoint, Status } from '../types';

const statuses = [
  { id: 1, name: 'To Do', color: '#3366cc' },
  { id: 2, name: 'In Progress', color: '#cc9933' },
  { id: 3, name: 'Done', color: '#33cc66' },
] as Status[];

// Noon UTC, as lib/cumulativeFlow.test.ts's own momentLabel fixtures are: the
// day is the same one regardless of which zone the test runs in.
const point: CumulativeFlowPoint = { at: '2026-10-12T12:00:00.000Z', counts: [3, 1, 2] };

const render = (p: CumulativeFlowPoint | null) =>
  renderToStaticMarkup(
    <CumulativeFlowPin point={p} columns={[1, 2, 3]} statuses={statuses} step="day" locale="en-GB" />,
  );

describe('CumulativeFlowPin', () => {
  it('prompts to pin a moment, and carries no total, before anything is pinned', () => {
    const html = render(null);

    expect(html).toContain('Click the chart to pin a moment.');
    expect(html).not.toContain('Total');
  });

  it('lists the moment, every column in board order with its count, and the total', () => {
    const html = render(point);

    expect(html).toContain('12 Oct');
    expect(html).toContain('To Do 3');
    expect(html).toContain('In Progress 1');
    expect(html).toContain('Done 2');
    expect(html).toContain('Total 6');

    const toDo = html.indexOf('To Do');
    const inProgress = html.indexOf('In Progress');
    const done = html.indexOf('Done 2');
    expect(toDo).toBeLessThan(inProgress);
    expect(inProgress).toBeLessThan(done);
  });
});
