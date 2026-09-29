import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { WipBands } from './WipBands';
import type { WipRun } from '../lib/wip';
import type { Wip } from '../types';

const section = (over: Partial<Wip> = {}): Wip => ({
  limit: 5,
  types: ['story', 'bug'],
  statusIds: [2, 3],
  load: 3,
  claimedInbound: 0,
  ...over,
});

const render = (runs: WipRun[], sec: Wip | null, load: number) => renderToStaticMarkup(<WipBands runs={runs} section={sec} load={load} />);

describe('WipBands', () => {
  it('draws one band at rest, tinted room, spanning the run', () => {
    const html = render([{ start: 2, statusIds: [2, 3] }], section(), 3);

    expect(html).toContain('WIP 3 of 5');
    expect(html).toContain('hatch-wip-band hatch-wip-room');
    expect(html).not.toContain('hatch-wip-preview');
    expect(html).toContain('grid-column:3 / span 2');
  });

  it('draws a preview, tinted and dashed, when the load differs from the section', () => {
    const html = render([{ start: 2, statusIds: [2, 3] }], section({ load: 3 }), 4);

    expect(html).toContain('WIP 4 of 5');
    expect(html).toContain('hatch-wip-tight');
    expect(html).toContain('hatch-wip-preview');
  });

  it('draws two bands with the same text for a split section', () => {
    const html = render(
      [
        { start: 1, statusIds: [2] },
        { start: 3, statusIds: [3] },
      ],
      section(),
      3,
    );

    expect(html.match(/WIP 3 of 5/g)).toHaveLength(2);
  });

  it('draws nothing with no section', () => {
    expect(render([{ start: 0, statusIds: [2] }], null, 3)).toBe('');
  });

  it('draws nothing with no runs', () => {
    expect(render([], section(), 3)).toBe('');
  });
});
