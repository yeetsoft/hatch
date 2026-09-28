/* The frame of the Monaco editor is CSS on a div, restating what `base.css`
   gives every input, select and textarea - because a div matches none of it. A
   restatement can drift from what it restates, and nothing but an eye would
   see it, so the four declarations that make it read as a Hatch field are held
   here. Read as text, the way confirmationsCss.test.ts reads App.css: there is
   no DOM in this runner to lay anything out in. */
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

describe('the editor frame', () => {
  const frame = declarationsOf('.hatch-md-editor');

  it('finds the rule it reads', () => {
    expect(Object.keys(frame).length).toBeGreaterThan(0);
  });

  it('is a field: hairline border, the control radius, the card ground', () => {
    expect(frame['border']).toBe('1px solid var(--line)');
    expect(frame['border-radius']).toBe('var(--r-ctl)');
    expect(frame['background']).toBe('var(--card)');
  });

  it('takes its font from tokens, which the component reads back for Monaco', () => {
    expect(frame['font-family']).toBe('var(--mono)');
    expect(frame['font-size']).toBe('var(--t-body)');
    expect(frame['line-height']).toBe('var(--lh-body)');
  });

  it('darkens the border on hover and rings it on focus, as a textarea does', () => {
    expect(declarationsOf('.hatch-md-editor:hover:not(:focus-within)')['border-color']).toBe('var(--line-strong)');

    const focused = declarationsOf('.hatch-md-editor:focus-within');
    expect(focused['border-color']).toBe('var(--primary)');
    expect(focused['box-shadow']).toBe('0 0 0 3px var(--primary-bg)');
  });
});
