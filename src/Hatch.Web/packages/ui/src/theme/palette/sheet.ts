/* The generated stylesheet: a parameter vector as the token file's own three blocks.
   ---------------------------------------------------------------------------
   Pure text, no DOM, so a test can read it back with parseTokenBlocks and the
   public site could build it without a document. themeStore.ts is the caller
   that puts it on a page.

   It restates only the colour tokens, in the token file's own selectors, so
   it wins by coming after tokens.css and nothing else about the cascade
   changes: the light `:root`, the dark palette under `prefers-color-scheme`
   guarded by `:root:not([data-theme='light'])`, and `:root[data-theme='dark']`.
   Both dark blocks take the one derived map, so they are identical by
   construction - the property tokens.test.ts holds the token file to. */
import { derivePalette } from './derive';
import type { Palette } from './pairs';
import type { PaletteParams } from './params';
import { colourTokens, type TokenBlocks } from './tokenBlocks';

/** One block's declarations, only the colour tokens of `block`, in authored order. */
const declarations = (palette: Palette, block: Record<string, string>): string =>
  colourTokens(block)
    .map((token) => `  ${token}: ${palette[token]};`)
    .join('\n');

/** The palette `params` makes of the token file's `blocks`, as CSS to follow tokens.css. */
export function paletteCss(params: PaletteParams, blocks: TokenBlocks): string {
  const light = declarations(derivePalette(params, blocks.light), blocks.light);
  const dark = declarations(derivePalette(params, blocks.dark), blocks.dark);
  const indented = dark.replace(/^/gm, '  ');
  return [
    `:root {\n${light}\n}`,
    `@media (prefers-color-scheme: dark) {\n  :root:not([data-theme='light']) {\n${indented}\n  }\n}`,
    `:root[data-theme='dark'] {\n${dark}\n}`,
    '',
  ].join('\n');
}
