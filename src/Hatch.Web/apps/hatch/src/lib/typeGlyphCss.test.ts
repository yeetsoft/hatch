/* The card's type tile gets its colour from the --type-* tokens in tokens.css
   alone - a hex literal here is a bug, per the brief on HA-547, and a --series-*
   token is not a type's to borrow. Read as text, the way
   claimCss.test.ts reads App.css - there is no DOM in this runner to lay
   anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { parseTokenBlocks } from '@hatch/ui/palette';
import { describe, expect, it } from 'vitest';

const tokens = parseTokenBlocks(
  readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8'),
);
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

describe('the board card type tile', () => {
  it('declares the five type tokens on :root and in both dark blocks', () => {
    for (const type of ['epic', 'story', 'task', 'bug', 'reflection']) {
      const token = `--type-${type}`;
      expect(tokens.light, token).toHaveProperty(token);
      expect(tokens.dark, token).toHaveProperty(token);
      expect(tokens.darkMedia, token).toHaveProperty(token);
    }
  });

  it('keeps story on the primary and bug on the danger, in all three places', () => {
    for (const block of [tokens.light, tokens.dark, tokens.darkMedia]) {
      expect(block['--type-story']).toBe('var(--primary)');
      expect(block['--type-bug']).toBe('var(--danger)');
    }
  });

  it('authors the other three as colours that differ between the themes', () => {
    for (const token of ['--type-epic', '--type-task', '--type-reflection']) {
      expect(tokens.light[token]).toMatch(/^#[0-9a-f]{6}$/);
      expect(tokens.dark[token]).toMatch(/^#[0-9a-f]{6}$/);
      expect(tokens.dark[token]).not.toBe(tokens.light[token]);
    }
  });

  it('has no per-type glyph colour rule in App.css', () => {
    expect(css).not.toMatch(/\.hatch-type-glyph--/);
    expect(css).not.toMatch(/\.hatch-type-glyph-shape/);
  });

  it('paints the picture in the accent ink', () => {
    expect(declarationsOf('.hatch-type-tile-picture')['fill']).toBe('var(--on-accent)');
    expect(declarationsOf('.hatch-type-tile-stroke')['stroke']).toBe('var(--on-accent)');
  });
});
