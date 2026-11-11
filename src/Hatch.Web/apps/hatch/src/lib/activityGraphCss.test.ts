/* The activity graph's rolling-mean line is the one stroke among fills, drawn
   in an SVG stretched with preserveAspectRatio="none". Without a non-scaling
   stroke the stretch would thicken and thin it with the viewport, so the rule
   that says so is asserted here. Read as text, the way barCss.test.ts reads
   App.css - there is no DOM in this runner to render anything in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim();
    }
  }
  return props;
}

describe('the activity graph line', () => {
  it('is a stroke that the stretch cannot distort', () => {
    const line = declarationsOf('.hatch-activity-line');

    expect(line['vector-effect']).toBe('non-scaling-stroke');
    expect(line.fill).toBe('none');
    expect(line.stroke).toMatch(/^var\(--/);
  });

  it('is round-capped, so a single bucket still draws a dot', () => {
    expect(declarationsOf('.hatch-activity-line')['stroke-linecap']).toBe('round');
  });

  it('leaves the bars as fills in house tokens', () => {
    expect(declarationsOf('.hatch-activity-bar').fill).toBe('var(--primary)');
    expect(declarationsOf('.hatch-activity-slot').fill).toBe('transparent');
  });
});
