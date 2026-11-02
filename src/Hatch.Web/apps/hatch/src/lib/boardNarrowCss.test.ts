/* A column that shows no card is drawn at half the width of one that does, and
   the change animates unless the reader asks for reduced motion. The ratio and
   the motion live in App.css, and there is no DOM in this runner to draw them
   in - so they are read as text, the way flowArrowsCss.test.ts reads it. The
   browser case (BoardNarrowColumnsTests) measures the drawn result. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

function parse(body: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const statement of body.split(';')) {
    const colon = statement.indexOf(':');
    if (colon === -1) continue;
    props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
  }
  return props;
}

/** The declarations of the rules whose selector list names exactly `selector`,
    merged, later winning - outside any at-rule. */
function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of css.matchAll(/(?<=^|[;}])\s*([^{};@]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    Object.assign(props, parse(body));
  }
  return props;
}

/** The declarations of `selector` inside the reduced-motion media block. */
function reducedMotionDeclarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, inner] of css.matchAll(/@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{((?:[^{}]*\{[^{}]*\})*)\s*\}/g)) {
    for (const [, selectors, body] of inner.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
      if (selectors.split(',').some((s) => s.trim() === selector)) Object.assign(props, parse(body));
    }
  }
  return props;
}

const track = /^minmax\(([\d.]+)rem, ([\d.]+)fr\)$/;

describe('the board narrows a column that shows no card', () => {
  const board = declarationsOf('.hatch-board');

  it('draws a narrow track at exactly half the full one, in minimum and share', () => {
    const full = track.exec(board['--column-track'] ?? '');
    const narrow = track.exec(board['--column-track-narrow'] ?? '');
    expect(full).not.toBeNull();
    expect(narrow).not.toBeNull();
    expect(Number(narrow![1]) * 2).toBeCloseTo(Number(full![1]));
    expect(Number(narrow![2]) * 2).toBeCloseTo(Number(full![2]));
  });

  it('keeps the full track as the grid default', () => {
    expect(board['grid-auto-columns']).toBe('var(--column-track)');
  });

  it('animates the track change, and not under prefers-reduced-motion', () => {
    expect(board.transition).toMatch(/grid-template-columns/);
    expect(reducedMotionDeclarationsOf('.hatch-board')).toHaveProperty('transition', 'none');
  });

  it('wraps the name of a narrow column rather than cutting it short', () => {
    const name = declarationsOf('.hatch-column--narrow .hatch-column-name');
    expect(name['white-space']).toBe('normal');
    expect(name['overflow-wrap']).toBe('anywhere');
  });
});
