/* A CSS colour as read off `getComputedStyle`, turned into the hex a theme
   object wants. Shared by `editorTheme.ts` (Monaco's theme) and
   `mermaidTheme.ts` (mermaid's), since both read the same house palette off
   the same computed styles and want the same `#RRGGBB`/`#RRGGBBAA`. */

const HEX = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;
const FUNCTIONAL =
  /^rgba?\(\s*(\d+(?:\.\d+)?)\s*[,\s]\s*(\d+(?:\.\d+)?)\s*[,\s]\s*(\d+(?:\.\d+)?)\s*(?:[,/]\s*(\d*\.?\d+)(%?)\s*)?\)$/i;

const byte = (n: number) =>
  Math.max(0, Math.min(255, Math.round(n)))
    .toString(16)
    .padStart(2, '0')
    .toUpperCase();

/**
 * A CSS colour as the `#RRGGBB` (or `#RRGGBBAA`, when it is not opaque) a
 * theme object reads. `#rgb`, `#rrggbb`, `rgb()` and `rgba()` are understood;
 * anything else - an unresolved `var(--x)`, an empty string, a keyword - is
 * `null`, so a caller can fall back rather than paint garbage.
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
