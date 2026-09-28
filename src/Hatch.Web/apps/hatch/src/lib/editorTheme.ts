/* The house tokens, as a Monaco theme.

   Monaco draws its own colours and takes them as hex only - `#RRGGBB` or
   `#RRGGBBAA`, and in a token rule with no `#` at all. The house's tokens are
   hex or `rgba()` and live in CSS, where the theme switch rewrites them. So
   this is the seam: a pure function from the token values a browser reports to
   the object `defineTheme` wants, with nothing here that needs a DOM or the
   editor - which is what lets it be tested in node, and what keeps `monaco-editor`
   out of any chunk but its own (a type-only import is erased).

   A token it cannot read does not throw. The entry is left out, and Monaco
   falls back to the colour its base theme (`vs` or `vs-dark`) already has for
   it: an editor slightly off-palette is a better failure than one that does not
   open. */

import type { editor } from 'monaco-editor/editor/editor.api';

/** The custom properties the theme reads, from `packages/ui/src/tokens.css`. A
    test holds that each is declared there, so renaming one is not silent. */
export const EDITOR_TOKEN_NAMES = [
  '--card',
  '--ink',
  '--muted',
  '--line',
  '--line-strong',
  '--primary',
  '--primary-bg',
  '--primary-ink',
  '--warn',
  '--warn-bg',
  '--warn-ink',
] as const;

export type EditorTokenName = (typeof EDITOR_TOKEN_NAMES)[number];

/** Each token as the browser reports it: hex or `rgba()`, maybe padded. */
export type EditorTokens = Readonly<Record<EditorTokenName, string>>;

const HEX = /^#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i;
const NUMBER = '(\\d+(?:\\.\\d+)?)';
const FUNCTIONAL = new RegExp(
  `^rgba?\\(\\s*${NUMBER}[,\\s]+${NUMBER}[,\\s]+${NUMBER}(?:\\s*[,/]\\s*${NUMBER}(%?))?\\s*\\)$`,
  'i',
);

const byte = (n: number) => Math.max(0, Math.min(255, Math.round(n)));
const pair = (n: number) => byte(n).toString(16).padStart(2, '0').toUpperCase();

/** `#RRGGBB` for an opaque colour, `#RRGGBBAA` for anything less. */
function format(r: number, g: number, b: number, a: number): string {
  const alpha = Math.max(0, Math.min(1, a));
  const opaque = byte(alpha * 255) === 255;
  return `#${pair(r)}${pair(g)}${pair(b)}${opaque ? '' : pair(alpha * 255)}`;
}

/**
 * A CSS colour as Monaco reads it: `#rgb`, `#rrggbb`, `rgb()` and `rgba()` in,
 * `#RRGGBB` or `#RRGGBBAA` out. Anything else - `var(--x)`, a keyword, empty -
 * is `null`. Whitespace either side is fine: `getPropertyValue` reports a
 * custom property with the space that followed its colon.
 */
export function toMonacoHex(css: string): string | null {
  const value = css.trim();

  const hex = HEX.exec(value);
  if (hex) {
    let digits = hex[1];
    if (digits.length <= 4) digits = [...digits].map((d) => d + d).join('');
    const n = (i: number) => parseInt(digits.slice(i, i + 2), 16);
    return format(n(0), n(2), n(4), digits.length === 8 ? n(6) / 255 : 1);
  }

  const fn = FUNCTIONAL.exec(value);
  if (fn) {
    const alpha = fn[4] === undefined ? 1 : fn[5] ? Number(fn[4]) / 100 : Number(fn[4]);
    return format(Number(fn[1]), Number(fn[2]), Number(fn[3]), alpha);
  }

  return null;
}

/** The same colour at `alpha` (0 to 1), whatever alpha it had. A value that is
    not a colour comes back as it was. */
export function withAlpha(color: string, alpha: number): string {
  const hex = toMonacoHex(color);
  if (!hex) return color;
  const n = (i: number) => parseInt(hex.slice(i, i + 2), 16);
  return format(n(1), n(3), n(5), alpha);
}

/** Reads every token the theme needs from anything that answers
    `getPropertyValue` - a `CSSStyleDeclaration`, which is what the caller has. */
export function readTokens(style: { getPropertyValue(name: string): string }): EditorTokens {
  return Object.fromEntries(EDITOR_TOKEN_NAMES.map((name) => [name, style.getPropertyValue(name)])) as EditorTokens;
}

/** A token rule's colour: opaque, six digits, no `#`. */
function ruleColor(css: string): string | undefined {
  const hex = toMonacoHex(css);
  return hex ? hex.slice(1, 7) : undefined;
}

/**
 * The theme for one appearance. `dark` picks the base whose colours fill in
 * whatever is not set here; everything the house cares about is set.
 *
 * The rules are the scopes Monaco's markdown grammar emits (read from
 * `languages/definitions/markdown/markdown.js`), not TextMate's: `keyword` is
 * a heading, a list marker or a rule, `variable` and `variable.source` are
 * inline code and a fence, `string.link` a link, `comment` a blockquote.
 */
export function editorTheme(tokens: EditorTokens, dark: boolean): editor.IStandaloneThemeData {
  const colors: Record<string, string> = {};
  const set = (key: string, css: string, alpha?: number) => {
    const hex = alpha === undefined ? toMonacoHex(css) : withAlpha(css, alpha);
    if (hex && HEX.test(hex)) colors[key] = hex;
  };

  set('editor.background', tokens['--card']);
  set('editor.foreground', tokens['--ink']);
  set('editorCursor.foreground', tokens['--primary']);
  set('editor.selectionBackground', tokens['--primary'], 0.25);
  set('editor.inactiveSelectionBackground', tokens['--primary-bg']);

  set('editorWidget.background', tokens['--card']);
  set('editorWidget.border', tokens['--line']);
  set('editorWidget.foreground', tokens['--ink']);
  set('input.background', tokens['--card']);
  set('input.border', tokens['--line']);
  set('input.foreground', tokens['--ink']);
  set('inputOption.activeBorder', tokens['--primary']);
  set('inputOption.activeBackground', tokens['--primary-bg']);
  set('focusBorder', tokens['--primary']);

  set('editor.findMatchBackground', tokens['--warn'], 0.35);
  set('editor.findMatchHighlightBackground', tokens['--warn-bg']);

  set('scrollbarSlider.background', tokens['--line-strong'], 0.5);
  set('scrollbarSlider.hoverBackground', tokens['--line-strong'], 0.7);
  set('scrollbarSlider.activeBackground', tokens['--line-strong'], 0.9);

  const rules: editor.ITokenThemeRule[] = [];
  const rule = (scopes: string[], css: string, fontStyle?: string) => {
    const foreground = ruleColor(css);
    if (!foreground && !fontStyle) return;
    for (const token of scopes) rules.push({ token, ...(foreground && { foreground }), ...(fontStyle && { fontStyle }) });
  };

  rule([''], tokens['--ink']);
  rule(['keyword'], tokens['--primary-ink']);
  rule(['strong'], tokens['--ink'], 'bold');
  rule(['emphasis'], tokens['--ink'], 'italic');
  rule(['variable', 'variable.source'], tokens['--warn-ink']);
  rule(['string.link'], tokens['--primary'], 'underline');
  rule(['string.target'], tokens['--primary']);
  rule(['comment'], tokens['--muted']);
  rule(
    ['keyword.table.header', 'keyword.table.left', 'keyword.table.middle', 'keyword.table.right'],
    tokens['--muted'],
  );
  rule(['tag', 'string.html', 'attribute.name.html', 'string.escape'], tokens['--muted']);

  return { base: dark ? 'vs-dark' : 'vs', inherit: true, rules, colors };
}
