/* Only an emergency's marks move, and reduced motion stills them. Read as text,
   the way pullCss.test.ts reads App.css - there is no DOM in this runner. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

const MOVING = [
  '.hatch-type-tile-flame--emergency .hatch-type-tile-flame-body',
  '.hatch-type-tile-flame--emergency .hatch-type-tile-flame-core',
  '.hatch-type-tile-trail--emergency',
];

/** Every rule outside an at-rule block, as its selectors and its declarations. */
function rules(source: string): { selectors: string[]; body: string }[] {
  return [...source.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(([, selectors, body]) => ({
    selectors: selectors.split(',').map((s) => s.trim().replace(/\s+/g, ' ')),
    body,
  }));
}

/** A declaration that sets something moving: `animation: none` is the opposite. */
const animates = (body: string) => /(^|;)\s*animation(-name)?\s*:(?!\s*none\s*(;|$))/.test(body);

describe('the moving marks of an emergency tile', () => {
  const animating = rules(css).filter(
    (rule) => animates(rule.body) && rule.selectors.some((s) => /hatch-type-(tile|glyph)/.test(s)),
  );

  it('animates the flame body, the flame core and the trail, and only behind an emergency', () => {
    const selectors = animating.flatMap((rule) => rule.selectors);
    expect(selectors.sort()).toEqual([...MOVING].sort());
    for (const rule of animating) {
      for (const selector of rule.selectors) expect(selector).toContain('--emergency');
    }
  });

  it('sets animation: none for each of them under prefers-reduced-motion: reduce', () => {
    const blocks = [...css.matchAll(/@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{((?:[^{}]*\{[^{}]*\})*)\s*\}/g)];
    const stilled = blocks
      .flatMap(([, inner]) => rules(inner))
      .filter((rule) => /(^|;)\s*animation\s*:\s*none\s*(;|$)/.test(rule.body))
      .flatMap((rule) => rule.selectors);
    for (const selector of MOVING) expect(stilled, selector).toContain(selector);
  });

  it('declares no animation on any other tile rule', () => {
    const others = rules(css).filter(
      (rule) =>
        animates(rule.body) &&
        rule.selectors.some((s) => /hatch-type-(tile|glyph)/.test(s)) &&
        !rule.selectors.every((s) => MOVING.includes(s)),
    );
    expect(others.map((rule) => rule.selectors)).toEqual([]);
  });

  it('keeps no opacity below 0.8 in the flicker, so the tile never blinks', () => {
    for (const name of ['hatch-flame-flicker', 'hatch-flame-core-flicker', 'hatch-trail-streak']) {
      const keyframes = new RegExp(`@keyframes ${name}\\s*\\{((?:[^{}]*\\{[^{}]*\\})*)\\s*\\}`).exec(css);
      expect(keyframes, name).not.toBeNull();
      const opacities = [...keyframes![1].matchAll(/opacity:\s*([\d.]+)/g)].map((m) => Number(m[1]));
      expect(opacities.length).toBeGreaterThan(0);
      expect(Math.min(...opacities), name).toBeGreaterThanOrEqual(0.8);
    }
  });
});
