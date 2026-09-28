/* The frame the editor wears, held where somebody restyling it would have to
   break it. The editor is Monaco, which draws its own text and nothing of its
   edge, so what makes it read as a Hatch field is this rule and not any option;
   and it can only be seen in a browser, so the declarations that make it a
   field are pinned here. Read as text, as confirmationsCss.test.ts reads it:
   there is no DOM in this runner to lay anything out in. */
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
  it('is a field: the house border, radius and card', () => {
    const frame = declarationsOf('.hatch-md-editor');

    expect(frame['border-radius']).toBe('var(--r-ctl)');
    expect(frame.border).toBe('1px solid var(--line)');
    expect(frame.background).toBe('var(--card)');
  });

  it('takes the primary border on focus, wherever inside it the focus is', () => {
    expect(declarationsOf('.hatch-md-editor:focus-within')['border-color']).toBe('var(--primary)');
  });

  it('takes the darker border while hovered and unfocused', () => {
    expect(declarationsOf('.hatch-md-editor:hover:not(:focus-within)')['border-color']).toBe('var(--line-strong)');
  });
});
