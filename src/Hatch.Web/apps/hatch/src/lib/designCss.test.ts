/* The rename HA-2250 made was a scripted one - every class selector under the
   "---- Design gallery ----" banner gets the design- prefix - and this is the
   rg for stragglers, turned into a test rather than a one-off command. Read as
   text, the way touchCss.test.ts reads App.css - there is no DOM in this
   runner to lay anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const BANNER = '/* ---- Design gallery ---- */';

const full = readFileSync(new URL('../App.css', import.meta.url), 'utf8');
const bannerAt = full.indexOf(BANNER);
if (bannerAt === -1) throw new Error(`App.css has no "${BANNER}" banner`);

const css = full.slice(bannerAt).replace(/\/\*[\s\S]*?\*\//g, '');

/** Every selector list that precedes a `{`, at-rules (`@media`, …) excluded:
    nesting is irrelevant here, since this only reads what is written
    immediately before each opening brace. */
function selectorLists(): string[] {
  return [...css.matchAll(/([^{};]+)\{/g)]
    .map((m) => m[1].trim())
    .filter((s) => !s.startsWith('@'));
}

/** The class that opens each compound unit of a selector - the part right
    after a combinator, a comma or the start of the list - so `.design-x.active`
    and `.design-x:hover` are read as `design-x` alone, with the chained
    `active` left to it rather than asked to carry its own prefix. */
function leadingClassesOf(selector: string): string[] {
  return [...selector.matchAll(/(?:^|[\s,>+~])\.([a-zA-Z][\w-]*)/g)].map((m) => m[1]);
}

const lists = selectorLists();

describe('the design gallery CSS block', () => {
  it('prefixes every class selector with design-', () => {
    const bare = lists.flatMap(leadingClassesOf).filter((name) => !name.startsWith('design-'));
    expect(bare).toEqual([]);
  });

  it('holds no component CSS of its own', () => {
    const allClasses = lists.flatMap((s) => [...s.matchAll(/\.([a-zA-Z][\w-]*)/g)].map((m) => m[1]));
    const componentClasses = allClasses.filter(
      (name) => name === 'card' || name === 'field' || name === 'btn' || name.startsWith('btn-'),
    );
    expect(componentClasses).toEqual([]);
  });
});
