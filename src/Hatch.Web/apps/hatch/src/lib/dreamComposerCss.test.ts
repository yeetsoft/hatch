/* The rules the Dreams composer's look rests on, read as text the way
   cardCss.test.ts reads App.css - there is no DOM in this runner to lay
   anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** The bodies of every block opened by `at`, as one string. */
function blocksOf(at: string): string {
  let out = '';
  for (let start = css.indexOf(at); start !== -1; start = css.indexOf(at, start + 1)) {
    let depth = 0;
    let i = css.indexOf('{', start);
    const from = i + 1;
    for (; i < css.length; i++) {
      if (css[i] === '{') depth++;
      if (css[i] === '}' && --depth === 0) break;
    }
    out += css.slice(from, i);
  }
  return out;
}

/** The declarations of every rule whose selector list names `selector` on its
    own, merged, later winning. `at` limits it to rules inside blocks opened by
    that at-rule. */
function declarationsOf(selector: string, at?: string): Record<string, string> {
  const scope = at ? blocksOf(at) : css;
  const props: Record<string, string> = {};
  for (const [, selectors, body] of scope.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

describe('the dream box', () => {
  for (const kind of ['hatch-description-editor', 'hatch-md-editor']) {
    it(`is borderless, transparent and 18px on .${kind}`, () => {
      const rule = declarationsOf(`.hatch-dream-box.${kind}`);
      expect(rule['border']).toBe('0');
      expect(rule['background']).toBe('transparent');
      expect(rule['font-size']).toBe('var(--t-subhead)');
    });
  }

  it('takes no focus ring', () => {
    expect(declarationsOf('.hatch-dream-box.hatch-md-editor:focus-within')['box-shadow']).toBe('none');
    expect(declarationsOf('.hatch-dream-box.hatch-description-editor:focus')['box-shadow']).toBe('none');
  });

  it('has no inset, for both bodies to read', () => {
    expect(declarationsOf('.hatch-dream-box')['--md-inset']).toBe('0');
  });
});

describe('the rule under the box', () => {
  it('is faint at rest and full while the box has focus', () => {
    expect(Number(declarationsOf('.hatch-dream-composer__box::after')['opacity'])).toBeLessThan(1);
    expect(declarationsOf('.hatch-dream-composer__box:focus-within::after')['opacity']).toBe('1');
  });

  it('is the gradient ink pair, and a solid Highlight under forced colours', () => {
    // Not through declarationsOf: it merges the forced-colours rule over this one.
    expect(css).toMatch(/\.hatch-dream-composer__box::after\s*\{[^}]*linear-gradient\(90deg, var\(--gradient-ink-from\), var\(--gradient-ink-to\)\)/);
    expect(declarationsOf('.hatch-dream-composer__box::after', '@media (forced-colors: active)')['background']).toBe('Highlight');
  });
});

describe('the fade', () => {
  it('moves nothing under reduced motion', () => {
    const rule = declarationsOf('.hatch-dream-composer__ghost', '@media (prefers-reduced-motion: reduce)');
    expect(rule['animation']).toBe('none');
  });
});

describe('Post', () => {
  it('is the chrome gradient, and brightens rather than swapping on hover', () => {
    expect(declarationsOf('.hatch-btn.hatch-gradient-fill')['background-image']).toContain('var(--chrome-end)');
    expect(declarationsOf('.hatch-btn.hatch-gradient-fill:hover:not(:disabled)')['filter']).toBe('brightness(1.1)');
  });
});
