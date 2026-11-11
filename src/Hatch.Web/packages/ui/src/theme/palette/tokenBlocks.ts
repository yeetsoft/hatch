/* The three palette blocks of tokens.css, read as text.
   ---------------------------------------------------------------------------
   The token file authors its palette as three blocks: the bare `:root` (light),
   and the dark palette twice, once under `prefers-color-scheme` and once under
   `[data-theme='dark']`. The palette engine takes one block at a time, and
   themeStore.ts and tokens.test.ts each used to carry their own copy of the
   regexes that find them. This is the one copy. */

/** A block's declarations, property to value, whitespace collapsed. */
export type TokenBlock = Record<string, string>;

export interface TokenBlocks {
  light: TokenBlock;
  /** The `[data-theme='dark']` block: the one active when ThemeProvider writes the attribute. */
  dark: TokenBlock;
  /** The `prefers-color-scheme` copy of the dark block, which tokens.test.ts holds identical to `dark`. */
  darkMedia: TokenBlock;
}

// The bare :root - light. Excludes :root[data-theme='dark'] and
// :root:not([data-theme='light']), neither of which has "{" right after "root".
const LIGHT = /:root\s*\{([^}]*)\}/;
const DARK_MEDIA = /:root:not\(\[data-theme=['"]light['"]\]\)\s*\{([^}]*)\}/;
const DARK_ATTR = /:root\[data-theme=['"]dark['"]\]\s*\{([^}]*)\}/;

function declarationsOf(css: string, selectorPattern: RegExp): TokenBlock {
  const match = selectorPattern.exec(css);
  if (!match) throw new Error(`tokens.css: no block matches ${selectorPattern}`);
  const props: TokenBlock = {};
  for (const statement of match[1].split(';')) {
    const trimmed = statement.trim();
    if (!trimmed) continue;
    const colon = trimmed.indexOf(':');
    if (colon === -1) continue;
    props[trimmed.slice(0, colon).trim()] = trimmed.slice(colon + 1).trim().replace(/\s+/g, ' ');
  }
  return props;
}

export function parseTokenBlocks(css: string): TokenBlocks {
  const bare = css.replace(/\/\*[\s\S]*?\*\//g, '');
  return {
    light: declarationsOf(bare, LIGHT),
    dark: declarationsOf(bare, DARK_ATTR),
    darkMedia: declarationsOf(bare, DARK_MEDIA),
  };
}

/** A value is a colour when it contains `#rrggbb` or `rgb(`/`rgba(`: radius, type, `color-scheme` and the rest are not. */
export const isColourValue = (value: string): boolean => /#[0-9a-f]{6}\b|rgba?\(/i.test(value);

/** The tokens of a block whose value is a colour, in authored order. */
export const colourTokens = (block: TokenBlock): string[] =>
  Object.keys(block).filter((prop) => prop.startsWith('--') && isColourValue(block[prop]));
