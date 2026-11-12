import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { makeScale } from '../lib/timeline';
import { TimeRuler } from './TimeRuler';

const scale = makeScale('2026-10-01T00:00:00.000Z', '2026-10-15T00:00:00.000Z');

const render = () =>
  renderToStaticMarkup(<TimeRuler scale={scale} offsetMinutes={0} locale="en-GB" onFrame={() => {}} />);

describe('TimeRuler', () => {
  it('draws the ruler with its ticks', () => {
    const html = render();

    expect(html).toContain('hatch-timeline-ruler');
    expect(html).toContain('hatch-timeline-ruler--brushable');
    expect(html).toContain('hatch-timeline-tick');
  });

  it('draws no brush at rest', () => {
    expect(render()).not.toContain('hatch-timeline-brush');
  });
});
