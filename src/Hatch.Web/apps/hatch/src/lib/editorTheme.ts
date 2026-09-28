/* The house palette, as a Monaco theme.

   Monaco draws its own text, so none of the page's CSS reaches inside it: the
   colours it paints with come from a theme object, and a theme object takes hex
   and nothing else. The house palette is authored as CSS custom properties -
   hex, or `rgba()` where a token is a wash - and changes with the theme, so
   this is the one place the two are joined.

   Pure on purpose. It takes the token *values* and returns the theme; reading
   them off the page is the component's business, at the moment it knows they
   are current. Nothing here imports `monaco-editor` at run time (a type-only
   import is erased), which is what lets it be tested in node and keeps it out
   of the editor's chunk. */

import type { editor } from 'monaco-editor/editor/editor.api';

/** The custom properties the theme reads. Guarded against tokens.css by a test. */
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

/** Each token's value as `getPropertyValue` returns it - leading space and all. */
export type EditorTokens = Record<EditorTokenName, string>;

const HEX = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;
const FUNCTIONAL =
  /^rgba?\(\s*(\d+(?:\.\d+)?)\s*[,\s]\s*(\d+(?:\.\d+)?)\s*[,\s]\s*(\d+(?:\.\d+)?)\s*(?:[,/]\s*(\d*\.?\d+)(%?)\s*)?\)$/i;

const byte = (n: number) =>
  Math.max(0, Math.min(255, Math.round(n)))
    .toString(16)
    .padStart(2, '0')
    .toUpperCase();

/**
 * A CSS colour as the `#RRGGBB` (or `#RRGGBBAA`, when it is not opaque) Monaco
 * reads. `#rgb`, `#rrggbb`, `rgb()` and `rgba()` are understood; anything else -
 * an unresolved `var(--x)`, an empty string, a keyword - is `null`, so a caller
 * can fall back rather than paint garbage.
 */
export function toMonacoHex(css: string): string | null {
  const value = css.trim();

  const hex = HEX.exec(value);
  if (hex) {
    const digits = hex[1].length === 3 ? [...hex[1]].map((c) => c + c).join('') : hex[1];
    return `#${digits.toUpperCase()}`;
  }

  const fn = FUNCTIONAL.exec(value);
  if (!fn) return null;
  const [, r, g, b, a, percent] = fn;
  const rgb = `#${byte(Number(r))}${byte(Number(g))}${byte(Number(b))}`;
  if (a === undefined) return rgb;
  const alpha = percent ? Number(a) / 100 : Number(a);
  return alpha >= 1 ? rgb : withAlpha(rgb, alpha);
}

/** A hex colour at the given opacity, 0 to 1: `#RRGGBBAA`. Any alpha it already had is replaced. */
export function withAlpha(hex: string, alpha: number): string {
  return `${hex.slice(0, 7).toUpperCase()}${byte(alpha * 255)}`;
}

/** A token's colour with no alpha and no `#`, which is the form a theme *rule* takes. */
function opaque(css: string): string | undefined {
  const hex = toMonacoHex(css);
  return hex ? hex.slice(1, 7) : undefined;
}

/**
 * Monaco's theme for the given token values. A token it cannot read is left
 * out, so that colour falls to the base theme (`vs` or `vs-dark`) - a wrong
 * colour in one corner is better than an editor that will not open.
 */
export function editorTheme(tokens: EditorTokens, dark: boolean): editor.IStandaloneThemeData {
  const colors: Record<string, string> = {};
  const put = (name: string, token: EditorTokenName, alpha?: number) => {
    const hex = toMonacoHex(tokens[token]);
    if (hex) colors[name] = alpha === undefined ? hex : withAlpha(hex, alpha);
  };

  put('editor.background', '--card');
  put('editor.foreground', '--ink');
  put('editorCursor.foreground', '--primary');
  put('editor.selectionBackground', '--primary', 0.25);
  put('editor.inactiveSelectionBackground', '--primary-bg');
  put('editorWidget.background', '--card');
  put('input.background', '--card');
  put('editorWidget.border', '--line');
  put('input.border', '--line');
  put('editorWidget.foreground', '--ink');
  put('input.foreground', '--ink');
  put('inputOption.activeBorder', '--primary');
  put('inputOption.activeBackground', '--primary-bg');
  put('focusBorder', '--primary');
  put('editor.findMatchBackground', '--warn', 0.35);
  put('editor.findMatchHighlightBackground', '--warn-bg');
  put('scrollbarSlider.background', '--line-strong', 0.4);
  put('scrollbarSlider.hoverBackground', '--line-strong', 0.7);
  put('scrollbarSlider.activeBackground', '--line-strong', 1);

  const rule = (token: string, from: EditorTokenName, fontStyle?: string): editor.ITokenThemeRule => {
    const foreground = opaque(tokens[from]);
    return {
      token,
      ...(foreground ? { foreground } : {}),
      ...(fontStyle ? { fontStyle } : {}),
    };
  };

  return {
    base: dark ? 'vs-dark' : 'vs',
    inherit: true,
    rules: [
      rule('', '--ink'),
      // Headings, list markers, rules.
      rule('keyword', '--primary-ink'),
      rule('strong', '--ink', 'bold'),
      rule('emphasis', '--ink', 'italic'),
      // Inline code and fences.
      rule('variable', '--warn-ink'),
      rule('variable.source', '--warn-ink'),
      rule('string.link', '--primary', 'underline'),
      rule('string.target', '--primary'),
      // Blockquotes and HTML comments, then the furniture: table pipes, raw
      // tags and escapes recede so the prose stays what is read.
      rule('comment', '--muted'),
      rule('keyword.table.header', '--muted'),
      rule('keyword.table.left', '--muted'),
      rule('keyword.table.middle', '--muted'),
      rule('keyword.table.right', '--muted'),
      rule('tag', '--muted'),
      rule('string.html', '--muted'),
      rule('attribute.name.html', '--muted'),
      rule('string.escape', '--muted'),
    ],
    colors,
  };
}
