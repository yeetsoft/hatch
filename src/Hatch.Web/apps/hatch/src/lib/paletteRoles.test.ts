/* Every colour token in tokens.css has a role in the palette engine, and every
   role names a token that is still in the file. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { colourTokens, parseTokenBlocks, ROLES } from '@hatch/ui/palette';
import { describe, expect, it } from 'vitest';

const { light, dark } = parseTokenBlocks(
  readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8'),
);

describe('palette roles', () => {
  for (const [name, block] of [['light :root', light], ["[data-theme='dark']", dark]] as const) {
    it(`gives every colour token in ${name} a role`, () => {
      for (const token of colourTokens(block)) {
        expect(ROLES, `${token} is a colour in ${name} but has no role in roles.ts`).toHaveProperty(token);
      }
    });
  }

  it('names only tokens that the light :root still declares', () => {
    for (const token of Object.keys(ROLES)) {
      expect(colourTokens(light), `roles.ts has a role for ${token}, which tokens.css no longer declares`).toContain(token);
    }
  });

  it('does not take radius, type or color-scheme for colours', () => {
    const colours = colourTokens(light);
    for (const token of ['--r', '--t-body', '--sp-4', '--transition', '--ui', '--mono', 'color-scheme']) {
      expect(colours).not.toContain(token);
    }
  });
});
