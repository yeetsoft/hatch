/* The capabilities bar on the Ambitions page and the Budget bar on an
   ambition's page are one bar: the same height, the same ends and the same
   track, written once and shared with the status meter's bar by selector list.
   The status meter keeps its own two sizes.
   Read as text, the way ambitionTokensCss.test.ts reads App.css - there is no
   DOM in this runner to draw them in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
const ambitionsPage = readFileSync(new URL('../pages/AmbitionsPage.tsx', import.meta.url), 'utf8');

/** The declarations of every rule whose selector list holds `selector` as one
    of its entries, merged, later winning. `.a .b` is its own entry, taken as
    written: it is not `.b`. */
function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of css.matchAll(/([^{}@;]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement
        .slice(colon + 1)
        .trim()
        .replace(/\s+/g, ' ');
    }
  }
  return props;
}

const ambition = declarationsOf('.hatch-ambition-bar');
const budget = declarationsOf('.hatch-budget-bar');

describe('the capabilities bar and the Budget bar', () => {
  it('are 12px high', () => {
    expect(ambition.height).toBe('12px');
    expect(budget.height).toBe('12px');
  });

  it('have the same round ends', () => {
    expect(ambition['border-radius']).toBe('var(--r-ctl)');
    expect(budget['border-radius']).toBe(ambition['border-radius']);
  });

  it('cut their first and last segments to the ends', () => {
    expect(ambition.overflow).toBe('hidden');
    expect(budget.overflow).toBe('hidden');
    expect(ambition.display).toBe('flex');
    expect(budget.display).toBe('flex');
  });

  it('write each shared declaration once, in the status meter bar rule', () => {
    const shared = [...css.matchAll(/([^{}@;]+)\{([^{}]*)\}/g)].filter(([, selectors]) =>
      selectors.split(',').some((s) => s.trim() === '.hatch-budget-bar'),
    );
    const heights = shared.filter(([, , body]) => /(^|;|\s)height:/.test(body));
    const radii = shared.filter(([, , body]) => /border-radius:/.test(body));

    expect(heights).toHaveLength(1);
    expect(radii).toHaveLength(1);
    expect(radii[0][1]).toContain('.hatch-ambition-bar');
    expect(radii[0][1]).toContain('.hatch-meter-lg .hatch-meter-bar');
  });

  it('share one track: the Budget track is the colour of the not started part', () => {
    expect(declarationsOf('.hatch-ambition-not-started').background).toBe('var(--line-strong)');
    expect(budget.background).toBe('var(--line-strong)');
  });
});

describe('the capabilities bar segments', () => {
  it('are at least 3px wide, with a surface-coloured line between two', () => {
    expect(declarationsOf('.hatch-ambition-segment')['min-width']).toBe('3px');
    expect(declarationsOf('.hatch-ambition-segment + .hatch-ambition-segment')['border-left']).toBe(
      '1px solid var(--card)',
    );
  });

  it('are divided by their counts alone', () => {
    expect(declarationsOf('.hatch-ambition-segment')['flex-basis']).toBe('0');
  });

  it('keep their four colours', () => {
    expect(declarationsOf('.hatch-ambition-holds').background).toBe('var(--success-fill)');
    expect(declarationsOf('.hatch-ambition-in-flight').background).toBe('var(--primary)');
    expect(declarationsOf('.hatch-ambition-waiting').background).toBe('var(--warn)');
    expect(declarationsOf('.hatch-ambition-not-started').background).toBe('var(--line-strong)');
  });

  it('keep their order', () => {
    const order = [...ambitionsPage.matchAll(/cls: '([a-z-]+)'/g)].map((m) => m[1]);

    expect(order).toEqual(['holds', 'in-flight', 'waiting', 'not-started']);
  });
});

describe('the Budget bar fill', () => {
  it('is the primary colour, and the warn ink once spent', () => {
    expect(declarationsOf('.hatch-budget-fill').background).toBe('var(--primary)');
    expect(declarationsOf('.hatch-budget-fill.hatch-budget-spent').background).toBe('var(--warn-ink)');
  });
});

describe('the status meter bar', () => {
  it('keeps its own sizes and ends', () => {
    expect(declarationsOf('.hatch-meter-bar')['border-radius']).toBe('var(--r-chip)');
    expect(declarationsOf('.hatch-meter-sm .hatch-meter-bar').height).toBe('6px');
    expect(declarationsOf('.hatch-meter-lg .hatch-meter-bar').height).toBe('20px');
    expect(declarationsOf('.hatch-meter-lg .hatch-meter-bar')['border-radius']).toBe('var(--r-ctl)');
  });

  it('keeps its segment floor and its line between segments', () => {
    expect(declarationsOf('.hatch-meter-segment')['min-width']).toBe('3px');
    expect(declarationsOf('.hatch-meter-segment + .hatch-meter-segment')['border-left']).toBe(
      '1px solid var(--card)',
    );
  });
});
