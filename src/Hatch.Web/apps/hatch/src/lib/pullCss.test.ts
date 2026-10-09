/* The pull-to-refresh indicator's two phases read differently without relying
   on the icon's animation, and that animation turns off under reduced motion
   - read as text, the way touchCss.test.ts reads App.css. There is no DOM in
   this runner to draw either phase in. */
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
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

describe('the pull-to-refresh indicator', () => {
  it('reads "ready" differently from "pulling" by more than its animation', () => {
    const pulling = declarationsOf('.hatch-pull-indicator--pulling');
    const ready = declarationsOf('.hatch-pull-indicator--ready');
    expect(ready.color).not.toBe(pulling.color);
  });

  it("turns its icon's pulse off under prefers-reduced-motion: reduce", () => {
    expect(declarationsOf('.hatch-pull-indicator--ready .hatch-pull-indicator__icon'))
      .toHaveProperty('animation', 'none');
  });
});
