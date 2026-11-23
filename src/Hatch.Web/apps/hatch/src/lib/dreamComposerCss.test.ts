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

describe('the weave section', () => {
  it('has a heading at the display size, in weight 800', () => {
    const rule = declarationsOf('.hatch-dream-weave__title');
    expect(rule['font-size']).toBe('var(--t-display)');
    expect(rule['font-weight']).toBe('800');
  });

  it('types its seed at 18px across the whole section, with no border', () => {
    const rule = declarationsOf('.hatch-dream-weave__seed .hatch-dream-weave__input');
    expect(rule['font-size']).toBe('var(--t-subhead)');
    expect(rule['width']).toBe('100%');
    expect(rule['border']).toBe('0');
    expect(rule['box-shadow']).toBe('none');
  });

  it('wears the dream box rule: faint at rest, full on focus, the ink pair, Highlight when forced', () => {
    expect(Number(declarationsOf('.hatch-dream-weave__seed::after')['opacity'])).toBeLessThan(1);
    expect(declarationsOf('.hatch-dream-weave__seed:focus-within::after')['opacity']).toBe('1');
    expect(css).toMatch(/\.hatch-dream-weave__seed::after,\s*\.hatch-dream-shape__words::after,\s*\.hatch-dream-composer__box::after\s*\{[^}]*linear-gradient\(90deg, var\(--gradient-ink-from\), var\(--gradient-ink-to\)\)/);
    expect(declarationsOf('.hatch-dream-weave__seed::after', '@media (forced-colors: active)')['background']).toBe('Highlight');
    // The shaping section's words box wears the same rule, in the same lists.
    expect(declarationsOf('.hatch-dream-shape__words:focus-within::after')['opacity']).toBe('1');
    expect(declarationsOf('.hatch-dream-shape__words::after', '@media (forced-colors: active)')['background']).toBe('Highlight');
  });

  it("moves the heading's gradient, forever, while a weave is being filed", () => {
    const rule = declarationsOf('.hatch-dream-weave__title--filing', '@supports (background-clip: text) or (-webkit-background-clip: text)');
    expect(rule['animation']).toMatch(/^hatch-dream-weave-filing \S+ linear infinite$/);
    expect(blocksOf('@keyframes hatch-dream-weave-filing')).toContain('background-position');
    // The loop has no seam: the first stop comes round again at the far end.
    expect(rule['background-image']).toMatch(/var\(--gradient-ink-from\), var\(--gradient-ink-to\), var\(--gradient-ink-from\)/);
  });

  it('holds the heading still under reduced motion and forced colours', () => {
    expect(declarationsOf('.hatch-dream-weave__title--filing', '@media (prefers-reduced-motion: reduce)')['animation']).toBe('none');
    const forced = declarationsOf('.hatch-dream-weave__title--filing', '@media (forced-colors: active)');
    expect(forced['animation']).toBe('none');
    expect(forced['background-image']).toBe('none');
  });

  it('is a section, not a fold', () => {
    expect(css).not.toMatch(/details\.hatch-dream-weave/);
    expect(css).not.toMatch(/\.hatch-dream-weave\s+summary/);
  });
});

describe('the weave mode pill', () => {
  it('shows the current mode hint in --mono, --t-label, --muted, with no coarse-pointer rule hiding it', () => {
    const rule = declarationsOf('.hatch-dream-weave__mode-hint');
    expect(rule['font-family']).toBe('var(--mono)');
    expect(rule['font-size']).toBe('var(--t-label)');
    expect(rule['color']).toBe('var(--muted)');
    expect(blocksOf('@media (pointer: coarse)')).not.toContain('hatch-dream-weave__mode-hint');
  });

  it('lists its steps in a column, not a wrap of chips', () => {
    expect(declarationsOf('.hatch-weave-mode-steps')['flex-direction']).toBe('column');
  });

  it('marks the current step in --chrome, not a borrowed type colour', () => {
    const rule = declarationsOf('.hatch-weave-mode-step.here');
    expect(rule['border-color']).toBe('var(--chrome)');
    expect(rule['background']).toContain('var(--chrome)');
  });
});

describe('the dream history', () => {
  it('fades in on opacity alone', () => {
    // Not through declarationsOf: it merges the reduced-motion rule over this one.
    expect(css).toMatch(/\.hatch-dream-history\s*\{[^}]*animation:\s*hatch-dream-history-in 200ms/);
    const frames = blocksOf('@keyframes hatch-dream-history-in');
    expect(frames).toContain('opacity');
    expect(frames).not.toContain('transform');
  });

  it('just appears under reduced motion', () => {
    expect(declarationsOf('.hatch-dream-history', '@media (prefers-reduced-motion: reduce)')['animation']).toBe('none');
  });

  it('draws its magnifier stroked, and its count in the mono face', () => {
    const glyph = declarationsOf('.hatch-dream-history-toggle svg');
    expect(glyph['fill']).toBe('none');
    expect(glyph['stroke']).toBe('currentColor');
    expect(declarationsOf('.hatch-dream-history-toggle__count')['font-family']).toBe('var(--mono)');
  });
});
