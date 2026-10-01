/* HA-200's regression test. Modal.css's `--framed` modifier and this app's
   own `.hatch-card` are two stylesheets' rules for the same element, and a
   plain last-rule-wins reader (declarationsOf, used by claimCss.test.ts and
   wipCss.test.ts) gets the wrong one once two selectors tie on specificity -
   which is exactly what was happening here. This resolves by specificity and
   then by source order, the same two steps a browser's cascade takes, over
   both stylesheets concatenated in the order the built bundle puts them:
   @hatch/ui's first, this app's own last. There is no DOM in this runner to
   lay a panel out in, so the cascade is read as text instead. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const strip = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');

const modalCss = strip(
  readFileSync(new URL('../../../../packages/ui/src/components/Modal.css', import.meta.url), 'utf8'),
);
const appCss = strip(readFileSync(new URL('../App.css', import.meta.url), 'utf8'));
const css = `${modalCss}\n${appCss}`;

interface Rule {
  classes: string[];
  specificity: number;
  order: number;
  declarations: Record<string, string>;
}

const rules: Rule[] = [];
let order = 0;
for (const [, selectors, body] of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
  const declarations: Record<string, string> = {};
  for (const statement of body.split(';')) {
    const colon = statement.indexOf(':');
    if (colon === -1) continue;
    declarations[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
  }
  for (const selector of selectors.split(',')) {
    const trimmed = selector.trim();
    // Only a compound of class names - `.a.b` - is a rule either competitor
    // writes for the panel; a descendant combinator, a pseudo-class or an
    // id never is, so it is skipped rather than mismatched.
    if (!/^(\.[-\w]+)+$/.test(trimmed)) continue;
    const classes = trimmed.slice(1).split('.');
    rules.push({ classes, specificity: classes.length, order: order++, declarations });
  }
}

/** The declaration an element wearing every one of `classes` ends up painted
    with for `property` - the highest-specificity match, ties broken by which
    rule comes later in the bundle. */
function paint(classes: string[], property: string): string | undefined {
  const worn = new Set(classes);
  const matching = rules.filter((r) => r.classes.every((c) => worn.has(c)) && property in r.declarations);
  if (matching.length === 0) return undefined;
  matching.sort((a, b) => a.specificity - b.specificity || a.order - b.order);
  return matching[matching.length - 1].declarations[property];
}

describe('a framed modal panel, wearing this app\'s own .hatch-card', () => {
  it('is a flex column - the framed modifier wins over the app\'s block card', () => {
    expect(paint(['hatch-card', 'hatch-modal__panel', 'hatch-modal__panel--framed'], 'display')).toBe('flex');
  });

  it('still clips to its own radius, not the card\'s overflow', () => {
    expect(paint(['hatch-card', 'hatch-modal__panel', 'hatch-modal__panel--framed'], 'overflow')).toBe('hidden');
  });
});
