/* The rules HA-182 rewrote: a claimed card's mark is the robot head's colour,
   not a filled dot, and the blink stays the nav control's alone. Read as
   text, the way wipCss.test.ts reads App.css - there is no DOM in this
   runner to lay anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** The declarations of every rule whose selector list names `selector` on its
    own - `.a` but not `.a-b`, and not `.a .b` - merged, later winning. */
function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

describe('a claimed card', () => {
  it('colours fresh blue and quiet amber, off the tokens rather than a literal', () => {
    expect(declarationsOf('.hatch-card-claim-fresh')['color']).toBe('var(--primary)');
    expect(declarationsOf('.hatch-card-claim-quiet')['color']).toBe('var(--warn)');
  });

  it('sets no background on the wrapper - the mark is a stroke now, not a fill', () => {
    expect(declarationsOf('.hatch-card-claim')).not.toHaveProperty('background');
    expect(declarationsOf('.hatch-card-claim-fresh')).not.toHaveProperty('background');
    expect(declarationsOf('.hatch-card-claim-quiet')).not.toHaveProperty('background');
  });
});

describe('the robot glyph', () => {
  it('reaches the blink only through the nav control, never on the bare part class', () => {
    expect(declarationsOf('.hatch-attention-glyph .hatch-robot-lamp.lit')).toHaveProperty('animation');
    expect(declarationsOf('.hatch-robot-lamp.lit')).not.toHaveProperty('animation');
    expect(declarationsOf('.hatch-card-claim-glyph')).not.toHaveProperty('animation');
  });
});
