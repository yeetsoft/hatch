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

  /* A type step is not themed: the public site's headline size is on :root
     alone, and sits between the display step and a page-sized heading. */
  it('declares --t-hero on :root only, between 40px and 48px', () => {
    expect(darkAttr).not.toHaveProperty('--t-hero');
    expect(darkMedia).not.toHaveProperty('--t-hero');
    expect(light).toHaveProperty('--t-hero');
    const size = Number.parseFloat(light['--t-hero']);
    expect(light['--t-hero']).toMatch(/px$/);
    expect(size).toBeGreaterThanOrEqual(40);
    expect(size).toBeLessThanOrEqual(48);
  });

  /* Fixed tokens: the console is dark in both themes. Somebody tidying the
     dark blocks "for completeness" would turn it white in one of them. */
  it('declares the console tokens on :root and in neither dark block', () => {
    const tokens = ['--console', '--console-line', '--on-console', '--console-dim', '--console-glow'];
    for (const prop of tokens) {
      expect(light, `${prop} is missing from :root`).toHaveProperty(prop);
      expect(darkAttr, `${prop} must not be redefined in [data-theme='dark']`).not.toHaveProperty(prop);
      expect(darkMedia, `${prop} must not be redefined under prefers-color-scheme`).not.toHaveProperty(prop);
    }
  });

  /* Themed tokens: the dark half is authored, not inherited. A token left on
     :root alone would draw the light shade on a dark card, and a light shade
     pasted into the dark blocks would too - so the dark value must differ. */
  it('declares the flow-viz tokens on :root and in both dark blocks, authored per theme', () => {
    const tokens = ['turn', 'family', 'person', 'other'].flatMap((kind) => [`--flow-${kind}`, `--on-flow-${kind}`]);
    for (const prop of tokens) {
      expect(light, `${prop} is missing from :root`).toHaveProperty(prop);
      expect(darkAttr, `${prop} is missing from [data-theme='dark']`).toHaveProperty(prop);
      expect(darkMedia, `${prop} is missing under prefers-color-scheme`).toHaveProperty(prop);
    }
    for (const kind of ['turn', 'family', 'person', 'other']) {
      const fill = `--flow-${kind}`;
      for (const [name, block] of [['light', light], ['dark', darkAttr]] as const) {
        expect(block[fill], `${fill} on ${name} must be an opaque 6-digit hex`).toMatch(/^#[0-9a-f]{6}$/i);
      }
      expect(darkAttr[fill], `${fill} in the dark blocks is the light shade`).not.toBe(light[fill]);
    }
  });

  it('carries the chrome tokens in both dark blocks', () => {
    expect(darkAttr).toHaveProperty('--chrome');
    expect(darkMedia).toHaveProperty('--chrome');
  });
});
