import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { RunnerGlyph } from './RunnerGlyph';
import type { RunnerGlyphState } from '../lib/runners';

const STATES: RunnerGlyphState[] = ['gone', 'exhausted', 'throttled', 'paused', 'stopping', 'working', 'idle', 'none'];

const draw = (state: RunnerGlyphState) => renderToStaticMarkup(<RunnerGlyph state={state} label={`label for ${state}`} />);

/** What is drawn inside the svg - the shape, without the state's name on the element. */
const shape = (html: string) => html.slice(html.indexOf('>') + 1);

describe('RunnerGlyph', () => {
  it.each(STATES)('is a labelled image naming its state: %s', (state) => {
    const html = draw(state);

    expect(html).toContain('role="img"');
    expect(html).toContain(`aria-label="label for ${state}"`);
    expect(html).toContain(`data-state="${state}"`);
    expect(html).toContain('class="hatch-runner-glyph');
  });

  it('draws a different shape for every state but paused and stopping', () => {
    const shapes = STATES.map((state) => shape(draw(state)));

    for (let a = 0; a < STATES.length; a++) {
      for (let b = a + 1; b < STATES.length; b++) {
        const sameShape = shapes[a] === shapes[b];
        const together = STATES[a] === 'paused' && STATES[b] === 'stopping';
        expect(sameShape, `${STATES[a]} and ${STATES[b]}`).toBe(together);
      }
    }
  });

  it('dashes the ring of a runner that never started', () => {
    expect(draw('none')).toContain('stroke-dasharray');
    expect(draw('idle')).not.toContain('stroke-dasharray');
  });

  it('draws no literal colour: the ink is the class\'s', () => {
    for (const state of STATES) expect(draw(state)).not.toMatch(/#[0-9a-f]{3,6}|rgb/i);
  });
});
