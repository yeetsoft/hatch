import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { WipBands } from './WipBands';
import type { WipRun } from '../lib/wip';
import type { Wip, WipSlice } from '../types';

const wipSlice = (over: Partial<WipSlice> = {}): WipSlice => ({
  types: ['story', 'bug'],
  limit: 5,
  load: 3,
  claimedInbound: 0,
  ...over,
});

const section = (over: Partial<Wip> = {}): Wip => ({
  statusIds: [2, 3],
  slices: [wipSlice(), wipSlice({ types: ['epic'], limit: null, load: 0 })],
  ...over,
});

const render = (runs: WipRun[], sec: Wip | null, loads: number[]) =>
  renderToStaticMarkup(<WipBands runs={runs} section={sec} loads={loads} />);

describe('WipBands', () => {
  it('draws one band at rest, tinted room, spanning the run', () => {
    const html = render([{ start: 2, statusIds: [2, 3] }], section(), [3, 0]);

    expect(html).toContain('WIP 3 of 5');
    expect(html).toContain('hatch-wip-band hatch-wip-room');
    expect(html).not.toContain('hatch-wip-preview');
    expect(html).toContain('grid-column:3 / span 2');
  });

  it('draws a preview, tinted and dashed, when a limited slice differs from its own load', () => {
    const html = render([{ start: 2, statusIds: [2, 3] }], section({ slices: [wipSlice({ load: 3 }), wipSlice({ types: ['epic'], limit: null, load: 0 })] }), [4, 0]);

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
      [3, 0],
    );

    expect(html.match(/WIP 3 of 5/g)).toHaveLength(2);
  });

  it('draws nothing with no section', () => {
    expect(render([{ start: 0, statusIds: [2] }], null, [3, 0])).toBe('');
  });

  it('draws nothing with no runs', () => {
    expect(render([], section(), [3, 0])).toBe('');
  });

  it('reads both slices when both are limited', () => {
    const html = render(
      [{ start: 2, statusIds: [2, 3] }],
      section({ slices: [wipSlice({ limit: 5, load: 3 }), wipSlice({ types: ['epic'], limit: 2, load: 1 })] }),
      [3, 1],
    );

    expect(html).toContain('WIP 3 of 5 · epics 1 of 2');
  });

  it('tints by the tighter of two limited slices', () => {
    const html = render(
      [{ start: 2, statusIds: [2, 3] }],
      section({ slices: [wipSlice({ limit: 5, load: 3 }), wipSlice({ types: ['epic'], limit: 2, load: 2 })] }),
      [3, 2],
    );

    expect(html).toContain('hatch-wip-full');
  });

  it('draws nothing when no slice has a limit', () => {
    const html = render(
      [{ start: 2, statusIds: [2, 3] }],
      section({ slices: [wipSlice({ limit: null }), wipSlice({ types: ['epic'], limit: null })] }),
      [3, 0],
    );

    expect(html).toBe('');
  });
});
