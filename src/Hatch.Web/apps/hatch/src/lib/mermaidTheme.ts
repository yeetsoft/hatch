/* The house palette, as a mermaid theme.

   `theme: 'base'` is the only mermaid theme that honours `themeVariables`, so
   it is the one named here; the rest are computed from these "primitive"
   variables unless also given explicitly (mermaid's own `theme-base.d.ts`).
   `--ui` and `--t-label` are not colours - a font stack and a size - so they
   are read as plain trimmed strings rather than parsed as colour.

   Pure on purpose, the same way `editorTheme.ts` is: it takes the token
   *values* and returns the theme; reading them off the page is the caller's
   business, at the moment it knows they are current. Nothing here imports
   `mermaid` at run time, which is what lets it be tested in a runner with no
   DOM and keeps it out of mermaid's own chunk. */

import { toMonacoHex } from './cssColor';

/** The custom properties the theme reads. Guarded against tokens.css by a test. */
export const MERMAID_TOKEN_NAMES = [
  '--card',
  '--bg',
  '--ink',
  '--muted',
  '--line',
  '--line-strong',
  '--primary',
  '--primary-bg',
  '--primary-ink',
  '--ui',
  '--t-label',
] as const;

export type MermaidTokenName = (typeof MERMAID_TOKEN_NAMES)[number];

/** Each token's value as `getPropertyValue` returns it - leading space and all. */
export type MermaidTokens = Record<MermaidTokenName, string>;

export interface MermaidTheme {
  theme: 'base';
  themeVariables: Record<string, string>;
}

/**
 * mermaid's theme for the given token values. A token it cannot read is left
 * out, so that colour falls to mermaid's own default - a wrong colour in one
 * corner is better than a diagram that will not draw.
 */
export function mermaidTheme(tokens: MermaidTokens): MermaidTheme {
  const themeVariables: Record<string, string> = {};
  const color = (name: string, token: MermaidTokenName) => {
    const hex = toMonacoHex(tokens[token]);
    if (hex) themeVariables[name] = hex;
  };
  const raw = (name: string, token: MermaidTokenName) => {
    const value = tokens[token]?.trim();
    if (value) themeVariables[name] = value;
  };

  color('background', '--bg');
  color('mainBkg', '--card');
  color('nodeBkg', '--card');
  color('clusterBkg', '--card');
  color('edgeLabelBackground', '--card');
  color('textColor', '--ink');
  color('nodeTextColor', '--ink');
  color('titleColor', '--ink');
  color('secondaryTextColor', '--muted');
  color('nodeBorder', '--line');
  color('clusterBorder', '--line');
  color('lineColor', '--line-strong');
  color('defaultLinkColor', '--line-strong');
  color('primaryColor', '--primary-bg');
  color('primaryBorderColor', '--primary');
  color('primaryTextColor', '--primary-ink');
  raw('fontFamily', '--ui');
  raw('fontSize', '--t-label');

  return { theme: 'base', themeVariables };
}
