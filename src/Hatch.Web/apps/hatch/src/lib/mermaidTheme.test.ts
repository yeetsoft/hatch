/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { MERMAID_TOKEN_NAMES, mermaidTheme, type MermaidTokens } from './mermaidTheme';

const light: MermaidTokens = {
  '--card': ' #ffffff',
  '--bg': ' #f5f5f5',
  '--ink': ' #1a1a1a',
  '--muted': ' #666666',
  '--line': ' #e0e0e0',
  '--line-strong': ' #d0d0d0',
  '--primary': ' #0066cc',
  '--primary-bg': ' rgba(0, 102, 204, 0.08)',
  '--primary-ink': ' #0052a3',
  '--ui': " 'Manrope Variable', 'Manrope', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
  '--t-label': ' 12px',
};

const dark: MermaidTokens = {
  '--card': ' #2d2d2d',
  '--bg': ' #1a1a1a',
  '--ink': ' #f5f5f5',
  '--muted': ' #999999',
  '--line': ' #404040',
  '--line-strong': ' #4d4d4d',
  '--primary': ' #66b3ff',
  '--primary-bg': ' rgba(102, 179, 255, 0.12)',
  '--primary-ink': ' #99ccff',
  '--ui': " 'Manrope Variable', 'Manrope', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
  '--t-label': ' 12px',
};

describe('mermaidTheme', () => {
  it('names the theme that honours themeVariables', () => {
    expect(mermaidTheme(light).theme).toBe('base');
    expect(mermaidTheme(dark).theme).toBe('base');
  });

  it('maps every token to its mermaid variable', () => {
    const { themeVariables } = mermaidTheme(dark);
    expect(themeVariables).toEqual({
      background: '#1A1A1A',
      mainBkg: '#2D2D2D',
      nodeBkg: '#2D2D2D',
      clusterBkg: '#2D2D2D',
      edgeLabelBackground: '#2D2D2D',
      textColor: '#F5F5F5',
      nodeTextColor: '#F5F5F5',
      titleColor: '#F5F5F5',
      secondaryTextColor: '#999999',
      nodeBorder: '#404040',
      clusterBorder: '#404040',
      lineColor: '#4D4D4D',
      defaultLinkColor: '#4D4D4D',
      primaryColor: '#66B3FF1F',
      primaryBorderColor: '#66B3FF',
      primaryTextColor: '#99CCFF',
      fontFamily: "'Manrope Variable', 'Manrope', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
      fontSize: '12px',
    });
  });

  it('leaves a token it cannot read out, rather than present as garbage', () => {
    const broken: MermaidTokens = { ...light, '--card': 'var(--nope)', '--ui': '', '--t-label': '  ' };
    const { themeVariables } = mermaidTheme(broken);
    expect(themeVariables).not.toHaveProperty('mainBkg');
    expect(themeVariables).not.toHaveProperty('nodeBkg');
    expect(themeVariables).not.toHaveProperty('clusterBkg');
    expect(themeVariables).not.toHaveProperty('edgeLabelBackground');
    expect(themeVariables).not.toHaveProperty('fontFamily');
    expect(themeVariables).not.toHaveProperty('fontSize');
    expect(themeVariables.background).toBe('#F5F5F5');
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
    for (const name of MERMAID_TOKEN_NAMES) {
      expect(root, name).toMatch(new RegExp(`(^|[\\s;])${name}\\s*:`));
    }
  });
});
