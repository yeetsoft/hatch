/* The mapping from house tokens to a Monaco theme is arithmetic and lookup, and
   is checked here; how the result looks is the operator's to judge in a
   browser. The last block holds the one thing a rename would silently break:
   that every token the theme reads is still declared. Read as text, the way
   tokens.test.ts reads tokens.css. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { EDITOR_TOKEN_NAMES, editorTheme, readTokens, toMonacoHex, withAlpha, type EditorTokens } from './editorTheme';

const light: EditorTokens = {
  '--card': ' #ffffff',
  '--ink': ' #1a1a1a',
  '--muted': ' #666666',
  '--line': ' #e0e0e0',
  '--line-strong': ' #d0d0d0',
  '--primary': ' #0066cc',
  '--primary-bg': ' rgba(0, 102, 204, 0.08)',
  '--primary-ink': ' #0052a3',
  '--warn': ' #b26a00',
  '--warn-bg': ' rgba(178, 106, 0, 0.08)',
  '--warn-ink': ' #8a5200',
};

const dark: EditorTokens = {
  '--card': ' #2d2d2d',
  '--ink': ' #f5f5f5',
  '--muted': ' #999999',
  '--line': ' #404040',
  '--line-strong': ' #4d4d4d',
  '--primary': ' #66b3ff',
  '--primary-bg': ' rgba(102, 179, 255, 0.12)',
  '--primary-ink': ' #99ccff',
  '--warn': ' #ffb74d',
  '--warn-bg': ' rgba(255, 183, 77, 0.14)',
  '--warn-ink': ' #ffd08a',
};

describe('toMonacoHex', () => {
  it('upper-cases hex', () => {
    expect(toMonacoHex('#0066cc')).toBe('#0066CC');
  });

  it('expands the short form', () => {
    expect(toMonacoHex('#fff')).toBe('#FFFFFF');
  });

  it('reads rgb()', () => {
    expect(toMonacoHex('rgb(26, 26, 26)')).toBe('#1A1A1A');
  });

  it('reads rgba() into eight digits', () => {
    expect(toMonacoHex('rgba(0, 102, 204, 0.08)')).toBe('#0066CC14');
    expect(toMonacoHex('rgba(102, 179, 255, 0.12)')).toBe('#66B3FF1F');
  });

  it('drops an alpha of one', () => {
    expect(toMonacoHex('rgba(0, 102, 204, 1)')).toBe('#0066CC');
  });

  it('tolerates the space getPropertyValue leaves in front', () => {
    expect(toMonacoHex(' #0066cc ')).toBe('#0066CC');
    expect(toMonacoHex(' rgba(0, 102, 204, 0.08)')).toBe('#0066CC14');
  });

  it('is null for what it cannot read', () => {
    expect(toMonacoHex('var(--x)')).toBeNull();
    expect(toMonacoHex('')).toBeNull();
    expect(toMonacoHex('rebeccapurple')).toBeNull();
  });
});

describe('withAlpha', () => {
  it('sets the alpha of an opaque colour', () => {
    expect(withAlpha('#0066cc', 0.25)).toBe('#0066CC40');
  });

  it('replaces the alpha a colour already had', () => {
    expect(withAlpha('rgba(0, 102, 204, 0.08)', 0.25)).toBe('#0066CC40');
  });

  it('hands back what it cannot read', () => {
    expect(withAlpha('var(--x)', 0.25)).toBe('var(--x)');
  });
});

describe('editorTheme', () => {
  it('picks the base by appearance', () => {
    expect(editorTheme(light, false).base).toBe('vs');
    expect(editorTheme(dark, true).base).toBe('vs-dark');
  });

  it('gives every colour as hex Monaco reads', () => {
    for (const [tokens, isDark] of [[light, false], [dark, true]] as const) {
      const colors = Object.entries(editorTheme(tokens, isDark).colors);
      expect(colors.length).toBeGreaterThan(10);
      for (const [key, value] of colors) expect(value, key).toMatch(/^#[0-9A-F]{6}([0-9A-F]{2})?$/);
    }
  });

  it('paints from the tokens it was given', () => {
    const { colors } = editorTheme(light, false);
    expect(colors['editor.background']).toBe('#FFFFFF');
    expect(colors['editor.foreground']).toBe('#1A1A1A');
    expect(colors['editorCursor.foreground']).toBe('#0066CC');
    expect(colors['editor.selectionBackground']).toBe('#0066CC40');
    expect(colors['editor.inactiveSelectionBackground']).toBe('#0066CC14');
    expect(editorTheme(dark, true).colors['editor.background']).toBe('#2D2D2D');
  });

  it('colours each markdown scope with an opaque six-digit foreground', () => {
    const { rules } = editorTheme(light, false);
    const tokens = rules.map((r) => r.token);
    for (const scope of [
      'keyword',
      'strong',
      'emphasis',
      'variable',
      'variable.source',
      'string.link',
      'comment',
      'keyword.table.header',
    ]) {
      expect(tokens, scope).toContain(scope);
    }
    for (const { token, foreground } of rules) expect(foreground, token).toMatch(/^[0-9A-F]{6}$/);
  });

  it('sets weight and style on the scopes that carry them', () => {
    const { rules } = editorTheme(light, false);
    expect(rules.find((r) => r.token === 'strong')?.fontStyle).toBe('bold');
    expect(rules.find((r) => r.token === 'emphasis')?.fontStyle).toBe('italic');
    expect(rules.find((r) => r.token === 'string.link')?.fontStyle).toBe('underline');
  });

  it('falls back rather than throwing on a token it cannot read', () => {
    const broken = Object.fromEntries(EDITOR_TOKEN_NAMES.map((name) => [name, 'var(--x)'])) as EditorTokens;
    const theme = editorTheme({ ...broken, '--ink': '' }, false);

    expect(theme.base).toBe('vs');
    expect(theme.colors).toEqual({});
    for (const { token, foreground } of theme.rules) expect(foreground ?? '', token).toMatch(/^([0-9A-F]{6})?$/);

    const partial = editorTheme({ ...light, '--primary': 'nonsense' }, false);
    expect(partial.colors).not.toHaveProperty('editorCursor.foreground');
    expect(partial.colors['editor.background']).toBe('#FFFFFF');
  });
});

describe('readTokens', () => {
  it('asks for every token by name', () => {
    const asked: string[] = [];
    const tokens = readTokens({
      getPropertyValue: (name) => {
        asked.push(name);
        return ` value${name}`;
      },
    });
    expect(asked).toEqual([...EDITOR_TOKEN_NAMES]);
    expect(tokens['--card']).toBe(' value--card');
  });
});

describe('the tokens the theme reads', () => {
  const css = readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8').replace(
    /\/\*[\s\S]*?\*\//g,
    '',
  );
  const root = /:root\s*\{([^}]*)\}/.exec(css)?.[1] ?? '';

  it('finds :root', () => {
    expect(root).not.toBe('');
  });

  it.each(EDITOR_TOKEN_NAMES)('%s is declared in :root', (name) => {
    expect(root).toMatch(new RegExp(`(^|[\\s;])${name}\\s*:`));
  });
});
