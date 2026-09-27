/* This lives in apps/hatch because packages/ui has no test runner and
   `make test-web` only loops over the apps. tokens.css's dark palette is
   authored twice on purpose - once under prefers-color-scheme, once under
   [data-theme='dark'] - so the manual override wins regardless of the OS. This
   is the thing that would otherwise only be caught by eye: the two blocks
   drifting apart, or a token declared in the dark blocks with no light answer
   at all. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8').replace(
  /\/\*[\s\S]*?\*\//g,
  '',
);

function declarationsOf(selectorPattern: RegExp): Record<string, string> {
  const match = selectorPattern.exec(css);
  if (!match) throw new Error(`No match for ${selectorPattern}`);
  const body = match[1];
  const props: Record<string, string> = {};
  for (const statement of body.split(';')) {
    const trimmed = statement.trim();
    if (!trimmed) continue;
    const colon = trimmed.indexOf(':');
    if (colon === -1) continue;
    const prop = trimmed.slice(0, colon).trim();
    const value = trimmed.slice(colon + 1).trim().replace(/\s+/g, ' ');
    props[prop] = value;
  }
  return props;
}

// The bare :root - light. Excludes :root[data-theme='dark'] and
// :root:not([data-theme='light']), neither of which has "{" right after "root".
const light = declarationsOf(/:root\s*\{([^}]*)\}/);
const darkMedia = declarationsOf(/:root:not\(\[data-theme=['"]light['"]\]\)\s*\{([^}]*)\}/);
const darkAttr = declarationsOf(/:root\[data-theme=['"]dark['"]\]\s*\{([^}]*)\}/);

describe('tokens.css', () => {
  it('keeps the two dark blocks identical', () => {
    expect(darkAttr).toEqual(darkMedia);
  });

  it('declares every dark token on the light :root too', () => {
    for (const prop of Object.keys(darkAttr)) {
      if (!prop.startsWith('--')) continue;
      expect(light, `${prop} is declared in the dark blocks but not on :root`).toHaveProperty(prop);
    }
  });

  it('carries the chrome tokens in both dark blocks', () => {
    expect(darkAttr).toHaveProperty('--chrome');
    expect(darkMedia).toHaveProperty('--chrome');
  });
});
