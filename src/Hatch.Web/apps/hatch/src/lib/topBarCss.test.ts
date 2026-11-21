/* The bar at phone width: HA-959's step down of its own padding and each
   side's gap by one token, read the way modalCss.test.ts reaches into
   @hatch/ui's own CSS from this app, and statusStepCss.test.ts splits
   inside/outside a shared @media query - there is no DOM in this runner to
   lay the bar out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(
  new URL('../../../../packages/ui/src/components/TopBar.css', import.meta.url),
  'utf8',
).replace(/\/\*[\s\S]*?\*\//g, '');

/** The declarations of `selector` as they read inside `@media (${mediaQuery})`
    blocks alone - copied from barCss.test.ts/statusStepCss.test.ts. The wrong
    tool for a selector with no rule inside such a block, but right here since
    both selectors below get one. */
function declarationsWithin(mediaQuery: string, selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  const needle = `@media (${mediaQuery}) {`;
  for (let start = css.indexOf(needle); start !== -1; start = css.indexOf(needle, start + needle.length)) {
    const openBrace = css.indexOf('{', start);
    let depth = 0;
    let end = openBrace;
    for (; end < css.length; end++) {
      if (css[end] === '{') depth++;
      else if (css[end] === '}') {
        depth--;
        if (depth === 0) break;
      }
    }
    const block = css.slice(openBrace + 1, end);

    for (const [, selectors, body] of block.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
      if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
      for (const statement of body.split(';')) {
        const colon = statement.indexOf(':');
        if (colon === -1) continue;
        props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
      }
    }
  }
  if (Object.keys(props).length === 0) throw new Error(`no @media (${mediaQuery}) block in TopBar.css names ${selector}`);
  return props;
}

/** The declarations of every rule naming `selector`, with every `@media { }`
    block's text removed first - same depth-counting walk as
    declarationsWithin, discarding the block instead of reading it. Needed
    because both selectors here carry a rule outside the query as well as one
    inside it, so a flat last-rule-wins merge over the raw file would report
    the 40rem value as if it were unconditional - copied from
    statusStepCss.test.ts's declarationsOutsideMedia. */
function declarationsOutsideMedia(selector: string): Record<string, string> {
  let plain = '';
  let i = 0;
  while (i < css.length) {
    const at = css.indexOf('@media', i);
    if (at === -1) {
      plain += css.slice(i);
      break;
    }
    plain += css.slice(i, at);
    const openBrace = css.indexOf('{', at);
    let depth = 0;
    let end = openBrace;
    for (; end < css.length; end++) {
      if (css[end] === '{') depth++;
      else if (css[end] === '}') {
        depth--;
        if (depth === 0) break;
      }
    }
    i = end + 1;
  }

  const props: Record<string, string> = {};
  for (const [, selectors, body] of plain.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

describe('the bar at phone width', () => {
  it('steps the inner padding down one token', () => {
    expect(declarationsWithin('max-width: 40rem', '.hatch-topbar__inner')['padding-inline']).toBe('var(--sp-4)');
  });

  it('steps each side\'s gap down one token', () => {
    expect(declarationsWithin('max-width: 40rem', '.hatch-topbar__side')['gap']).toBe('var(--sp-2)');
  });
});

describe('the bar outside phone width', () => {
  it('keeps the inner padding at its current token', () => {
    expect(declarationsOutsideMedia('.hatch-topbar__inner')['padding-inline']).toBe('var(--sp-5)');
  });

  it('keeps each side\'s gap at its current token', () => {
    expect(declarationsOutsideMedia('.hatch-topbar__side')['gap']).toBe('var(--sp-3)');
  });
});

/* HA-2447: between the phone breakpoint and about 1000px the nav, the scope
   control and the trailing controls do not fit one row, so the bar wraps
   instead of painting the nav over them. The box measurements are
   BarFitsDeskTests in Hatch.Api.Tests/E2e; this is the rules that make them
   hold. */
describe('the bar wraps rather than overlap', () => {
  it('lets the bar break onto a second row, with air between and around the rows', () => {
    const inner = declarationsOutsideMedia('.hatch-topbar__inner');
    expect(inner['flex-wrap']).toBe('wrap');
    expect(inner['row-gap']).toBe('var(--sp-1)');
    expect(inner['padding-block']).toBe('var(--sp-1)');
  });

  it('keeps the row gap where the menu bar\'s own gap would otherwise reset it', () => {
    expect(declarationsOutsideMedia('.hatch-topbar__inner.hatch-menu-bar')['row-gap']).toBe('var(--sp-1)');
  });

  it('lets the leading side break among its own items', () => {
    expect(declarationsOutsideMedia('.hatch-topbar__side')['flex-wrap']).toBe('wrap');
  });

  it('keeps the trailing side at the right edge when it drops to the second row', () => {
    expect(declarationsOutsideMedia('.hatch-topbar__side--end')['margin-left']).toBe('auto');
  });

  it('leaves the phone block without a wrap of its own', () => {
    expect(declarationsWithin('max-width: 40rem', '.hatch-topbar__inner')['flex-wrap']).toBeUndefined();
    expect(declarationsWithin('max-width: 40rem', '.hatch-topbar__side')['flex-wrap']).toBeUndefined();
  });
});
