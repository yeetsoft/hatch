/* The issue page's subheader: the context group - project, key, type, parent -
   drawn larger than the secondary row below it. Read as text, the way
   wipCss.test.ts and confirmationsCss.test.ts read App.css - there is no DOM
   in this runner to lay anything out in. See HA-328 §5, HA-333. */
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

describe('the issue subheader', () => {
  it('draws the identity group at --t-subhead or larger', () => {
    expect(declarationsOf('.hatch-issue-identity')['font-size']).toBe('var(--t-subhead)');
  });

  it('wraps the identity group rather than squeezing or truncating it', () => {
    expect(declarationsOf('.hatch-issue-identity')['flex-wrap']).toBe('wrap');
  });

  it('leaves .hatch-issue-meta exactly as it was', () => {
    expect(declarationsOf('.hatch-issue-meta')).toEqual({
      display: 'flex',
      'align-items': 'center',
      gap: 'var(--sp-3)',
      'flex-wrap': 'wrap',
    });
  });
});
