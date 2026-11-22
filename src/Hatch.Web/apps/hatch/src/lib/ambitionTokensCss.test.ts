/* The ambition pages' rules write no type size, radius, colour or space by
   hand: each one reads a token from packages/ui/src/tokens.css. There is no
   DOM in this runner to draw them in, so App.css is read as text, the way
   ballotCss.test.ts reads it. A rule added later under one of the six
   prefixes is checked too, since the rules are found by prefix. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

const PREFIXES = ['ambition', 'capability', 'pass', 'change', 'budget', 'epics-in-flight'];

/* A class of one of the six families. The lookahead keeps `.hatch-changelog-*`,
   which is another page's, out, and still takes a bare `.hatch-pass`. */
const FAMILY = /\.hatch-(?:ambition|capability|pass|change|budget|epics-in-flight)(?:-[a-z0-9-]*)?(?![\w-])/;

/* Names that share a prefix with a family by accident. `.hatch-change-toggle`
   is the Bulk page's date tick (pages/BulkPage.tsx), not the ambition pages'. */
const NOT_THESE_PAGES = new Set(['.hatch-change-toggle']);

interface Rule {
  selector: string;
  declarations: Array<[string, string]>;
}

function parse(body: string): Array<[string, string]> {
  const out: Array<[string, string]> = [];
  for (const statement of body.split(';')) {
    const colon = statement.indexOf(':');
    if (colon === -1) continue;
    out.push([statement.slice(0, colon).trim(), statement.slice(colon + 1).trim().replace(/\s+/g, ' ')]);
  }
  return out;
}

/* Every rule body in the file. A rule inside `@media (...) { ... }` matches
   too: the at-rule's prelude holds a `@`, which the selector group refuses, so
   the match starts again at the rule inside it. */
function familyRules(): Rule[] {
  const rules: Rule[] = [];
  for (const [, selectorList, body] of css.matchAll(/([^{}@;]+)\{([^{}]*)\}/g)) {
    const selectors = selectorList.split(',').map((s) => s.trim());
    if (selectors.every((s) => NOT_THESE_PAGES.has(s))) continue;
    if (!selectors.some((s) => FAMILY.test(s))) continue;
    rules.push({ selector: selectors.join(', '), declarations: parse(body) });
  }
  return rules;
}

const rules = familyRules();

/** A value with its `var(...)` groups taken out, so what is left is what was written by hand. */
const withoutVars = (value: string): string => value.replace(/var\([^)]*\)/g, '').trim();

/** Every `var(--x)` in a value, as the names `--x`. */
const varsIn = (value: string): string[] => [...value.matchAll(/var\(\s*(--[\w-]+)/g)].map((m) => m[1]);

const parts = (value: string): string[] => value.split(' ').filter(Boolean);

const COLOUR_PROPERTIES = new Set([
  'color',
  'background',
  'background-color',
  'border',
  'border-top',
  'border-right',
  'border-bottom',
  'border-left',
  'border-color',
]);
const RADIUS_PROPERTIES = new Set([
  'border-radius',
  'border-top-left-radius',
  'border-top-right-radius',
  'border-bottom-left-radius',
  'border-bottom-right-radius',
]);
const SPACE_PROPERTIES = /^(?:margin|padding)(?:-(?:top|right|bottom|left|inline|block)(?:-(?:start|end))?)?$|^(?:gap|row-gap|column-gap)$/;

/** True when the declaration writes something by hand that a token covers. */
function offends(property: string, value: string): boolean {
  if (property === 'font-size') {
    return !/^var\(--t-[\w-]+\)$/.test(value);
  }
  if (RADIUS_PROPERTIES.has(property)) {
    return !parts(value).every((p) => /^var\(--r-[\w-]+\)$/.test(p) || p === '50%' || p === '0');
  }
  if (SPACE_PROPERTIES.test(property)) {
    return !parts(value).every((p) => /^var\(--sp-[\w-]+\)$/.test(p) || p === '0' || p === 'auto');
  }
  if (COLOUR_PROPERTIES.has(property)) {
    const left = withoutVars(value)
      .replace(/\b(?:inherit|currentColor|transparent|none|solid|dashed|dotted)\b/gi, '')
      .replace(/\b\d+px\b/g, '')
      .trim();
    // A colour token is any other custom property; the size ladders are not colours.
    const wrongToken = varsIn(value).some((name) => /^--(?:t|r|sp)-/.test(name));
    return left !== '' || wrongToken;
  }
  return false;
}

const failures: Array<{ property: string; text: string }> = [];
for (const { selector, declarations } of rules) {
  for (const [property, value] of declarations) {
    if (offends(property, value)) failures.push({ property, text: `${selector} { ${property}: ${value} }` });
  }
}

const failuresFor = (test: (property: string) => boolean): string[] =>
  failures.filter((f) => test(f.property)).map((f) => f.text);

describe("the ambition pages' stylesheet", () => {
  it('sets every type size from the type ladder', () => {
    expect(failuresFor((p) => p === 'font-size')).toEqual([]);
  });

  it('sets every radius from the radius ladder', () => {
    expect(failuresFor((p) => RADIUS_PROPERTIES.has(p))).toEqual([]);
  });

  it('sets every colour from a token', () => {
    expect(failuresFor((p) => COLOUR_PROPERTIES.has(p))).toEqual([]);
  });

  it('sets every margin, padding and gap from the space ladder', () => {
    expect(failuresFor((p) => SPACE_PROPERTIES.test(p))).toEqual([]);
  });

  it.each(PREFIXES)('finds rules under the %s prefix', (prefix) => {
    const named = rules.filter((r) => r.selector.includes(`.hatch-${prefix}`));

    expect(named.length).toBeGreaterThan(0);
  });

  it("leaves another page's rules alone", () => {
    expect(rules.filter((r) => r.selector.includes('changelog'))).toEqual([]);
    expect(rules.filter((r) => r.selector.includes('.hatch-change-toggle') && !r.selector.includes(','))).toEqual([]);
  });
});
