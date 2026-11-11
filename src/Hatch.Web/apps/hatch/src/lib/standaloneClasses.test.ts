/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

/* standalone/topbar.ts restates the React components' markup in DOM calls and
   may not invent a class name: every `hatch-` literal in it has to be one a
   component under packages/ui/src/components declares. */
const ui = new URL('../../../../packages/ui/src/', import.meta.url);
const components = new URL('components/', ui);

describe('standalone/topbar.ts', () => {
  const standalone = readFileSync(new URL('standalone/topbar.ts', ui), 'utf8');
  const declared = readdirSync(components)
    .filter((file) => file.endsWith('.tsx'))
    .map((file) => readFileSync(new URL(file, components), 'utf8'))
    .join('\n');

  /* A class literal is a hatch- word inside quotes or a template; the comments
     mention hatch- names in prose, so look only at quoted strings. */
  const literals = new Set(
    [...standalone.matchAll(/(['"`])((?:(?!\1).)*)\1/g)]
      .flatMap((match) => match[2].split(/\s+/))
      .filter((word) => /^hatch-[a-z][a-z0-9_-]*$/.test(word)),
  );

  it('has class literals to check', () => {
    expect(literals.size).toBeGreaterThan(10);
    expect(literals).toContain('hatch-theme-picker__line');
  });

  /* A modifier a component composes (`hatch-menu__panel--${align}`) is declared
     by the template that builds it. */
  const isDeclared = (name: string) => {
    if (declared.includes(name)) return true;
    const at = name.lastIndexOf('--');
    return at > 0 && declared.includes(`${name.slice(0, at + 2)}\${`);
  };

  it.each([...literals])('declares %s in a component', (name) => {
    expect(isDeclared(name)).toBe(true);
  });

  it('would notice a class no component declares', () => {
    expect(isDeclared('hatch-invented__thing')).toBe(false);
  });
});
