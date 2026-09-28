/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { EDITOR_TOKEN_NAMES, editorTheme, toMonacoHex, withAlpha, type EditorTokens } from './editorTheme';

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
  it('upper-cases a long hex and expands a short one', () => {
    expect(toMonacoHex('#0066cc')).toBe('#0066CC');
    expect(toMonacoHex('#fff')).toBe('#FFFFFF');
  });

  it('reads rgb() and rgba()', () => {
    expect(toMonacoHex('rgb(26, 26, 26)')).toBe('#1A1A1A');
    expect(toMonacoHex('rgba(0, 102, 204, 0.08)')).toBe('#0066CC14');
    expect(toMonacoHex('rgba(102, 179, 255, 0.12)')).toBe('#66B3FF1F');
  });

  it('drops the alpha when it is 1', () => {
    expect(toMonacoHex('rgba(0, 102, 204, 1)')).toBe('#0066CC');
  });

  it('tolerates the whitespace getPropertyValue leaves on a token', () => {
    expect(toMonacoHex('  #0066cc ')).toBe('#0066CC');
  });

  it('answers null for what it cannot read', () => {
    expect(toMonacoHex('var(--x)')).toBeNull();
    expect(toMonacoHex('')).toBeNull();
    expect(toMonacoHex('tomato')).toBeNull();
  });
});

describe('withAlpha', () => {
  it('appends the alpha as two hex digits', () => {
    expect(withAlpha('#0066cc', 0.25)).toBe('#0066CC40');
  });

  it('replaces an alpha the colour already had', () => {
    expect(withAlpha('#0066CC14', 1)).toBe('#0066CCFF');
  });
});

describe('editorTheme', () => {
  it('is built on the base that matches the palette', () => {
    expect(editorTheme(light, false).base).toBe('vs');
    expect(editorTheme(dark, true).base).toBe('vs-dark');
    expect(editorTheme(light, false).inherit).toBe(true);
  });

  it('gives Monaco hex and nothing else', () => {
    for (const [tokens, isDark] of [[light, false], [dark, true]] as const) {
      const theme = editorTheme(tokens, isDark);
      expect(Object.keys(theme.colors).length).toBeGreaterThan(10);
      for (const [name, value] of Object.entries(theme.colors)) {
        expect(value, name).toMatch(/^#[0-9A-F]{6}([0-9A-F]{2})?$/);
      }
      for (const rule of theme.rules) {
        if (rule.foreground !== undefined) expect(rule.foreground, rule.token).toMatch(/^[0-9A-F]{6}$/);
      }
    }
  });

  it('paints the field in the house tokens', () => {
    const { colors } = editorTheme(dark, true);
    expect(colors['editor.background']).toBe('#2D2D2D');
    expect(colors['editor.foreground']).toBe('#F5F5F5');
    expect(colors['editorCursor.foreground']).toBe('#66B3FF');
    expect(colors['editor.selectionBackground']).toBe('#66B3FF40');
    expect(colors['editorWidget.border']).toBe('#404040');
  });

  it('has a rule for each scope the markdown grammar emits', () => {
    const tokens = editorTheme(light, false).rules.map((r) => r.token);
    for (const token of [
      'keyword',
      'strong',
      'emphasis',
      'variable',
      'variable.source',
      'string.link',
      'comment',
      'keyword.table.header',
    ]) {
      expect(tokens).toContain(token);
    }
  });

  it('bolds strong, italicises emphasis and underlines links', () => {
    const rules = editorTheme(light, false).rules;
    expect(rules.find((r) => r.token === 'strong')?.fontStyle).toBe('bold');
    expect(rules.find((r) => r.token === 'emphasis')?.fontStyle).toBe('italic');
    expect(rules.find((r) => r.token === 'string.link')?.fontStyle).toBe('underline');
  });

  it('falls back to the base theme for a token it cannot read, rather than throwing', () => {
    const broken = { ...light, '--card': 'var(--nope)', '--warn-ink': '' };
    const theme = editorTheme(broken, false);
    expect(theme.colors).not.toHaveProperty('editor.background');
    expect(theme.rules.find((r) => r.token === 'variable')).not.toHaveProperty('foreground');
    expect(theme.colors['editor.foreground']).toBe('#1A1A1A');
  });
});

describe('the tokens it reads', () => {
  const css = readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8').replace(
    /\/\*[\s\S]*?\*\//g,
    '',
  );
  const root = /:root\s*\{([^}]*)\}/.exec(css)?.[1] ?? '';

  it('are all declared in :root', () => {
    expect(root.length).toBeGreaterThan(0);
    for (const name of EDITOR_TOKEN_NAMES) {
      expect(root, name).toMatch(new RegExp(`(^|[\\s;])${name}\\s*:`));
    }
  });
});
