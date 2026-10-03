/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { applyChoice, applyThemeColor, themeColorFor } from '@hatch/ui';

// Mirrors tokens.test.ts's declarationsOf rather than importing it, to keep
// per-file independence.
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

const light = declarationsOf(/:root\s*\{([^}]*)\}/);
const darkAttr = declarationsOf(/:root\[data-theme=['"]dark['"]\]\s*\{([^}]*)\}/);

afterEach(() => vi.unstubAllGlobals());

describe('themeColorFor', () => {
  it('matches tokens.css for light', () => {
    expect(themeColorFor('light')).toBe(light['--chrome']);
  });

  it('matches tokens.css for dark', () => {
    expect(themeColorFor('dark')).toBe(darkAttr['--chrome']);
  });
});

describe('applyThemeColor', () => {
  /* This workspace's tests run in plain Node (no jsdom) - document itself is
     absent. No stub: the same no-DOM-at-all case viewport.test.ts exercises
     for matchesPhone. */
  it('does not throw where document is absent', () => {
    expect(() => applyThemeColor('light')).not.toThrow();
  });
});

/** A document stubbed with just what applyThemeColor and applyChoice use. */
function stubDocument() {
  let override: { attrs: Record<string, string> } | null = null;
  const root = {
    attrs: {} as Record<string, string | undefined>,
    setAttribute(name: string, value: string) {
      this.attrs[name] = value;
    },
    removeAttribute(name: string) {
      delete this.attrs[name];
    },
  };
  const meta = {
    attrs: {} as Record<string, string>,
    setAttribute(name: string, value: string) {
      this.attrs[name] = value;
    },
    remove() {
      override = null;
    },
  };
  const document = {
    documentElement: root,
    head: {
      querySelector: () => (override ? meta : null),
      prepend: () => {
        override = { attrs: meta.attrs };
      },
    },
    createElement: () => meta,
  };
  vi.stubGlobal('document', document);
  return { root, meta, overridden: () => override !== null };
}

describe('applyChoice', () => {
  it('sets data-theme and an override meta together for light', () => {
    const { root, meta, overridden } = stubDocument();

    applyChoice('light');

    expect(root.attrs['data-theme']).toBe('light');
    expect(overridden()).toBe(true);
    expect(meta.attrs['content']).toBe(light['--chrome']);
    expect(meta.attrs['name']).toBe('theme-color');
  });

  it('sets data-theme and an override meta together for dark', () => {
    const { root, meta, overridden } = stubDocument();

    applyChoice('dark');

    expect(root.attrs['data-theme']).toBe('dark');
    expect(overridden()).toBe(true);
    expect(meta.attrs['content']).toBe(darkAttr['--chrome']);
  });

  it('clears data-theme and leaves no override meta for auto', () => {
    const { root, overridden } = stubDocument();

    applyChoice('light');
    applyChoice('auto');

    expect(root.attrs['data-theme']).toBeUndefined();
    expect(overridden()).toBe(false);
  });
});
