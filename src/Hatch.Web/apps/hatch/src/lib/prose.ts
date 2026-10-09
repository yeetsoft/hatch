/* Where a description or comment gets cut for the three-block preview
   (HA-332). Splits the markdown source at a top-level block boundary rather
   than measuring rendered height - a ref and a ResizeObserver would decide
   "does this need a More button" after layout, a frame late, and could not
   be pinned by a test in this workspace (plain Node, no jsdom). Splitting the
   source is a pure function over a string instead.

   Built on marked.lexer(), not a hand-rolled splitter: it already tokenizes
   the source into top-level blocks - paragraph, heading, code, list,
   blockquote, table, … - each token's `.raw` the exact source substring, and
   a fence, a loose list, a blockquote or a table always comes back as one
   token however many blank lines sit inside it. So a cut here is never a cut
   through the middle of one of those.

   The head this produces still goes through lib/markdown.ts's marked ->
   DOMPurify pair before it is ever rendered, same as the whole source would -
   an unbalanced construct can render oddly but never unsafely. */

import { marked } from 'marked';

/** The source's top-level blocks, in order. Blank runs between them (and
    around them) are dropped. */
export function splitBlocks(markdown: string): string[] {
  return marked.lexer(markdown)
    .filter((token) => token.type !== 'space')
    .map((token) => token.raw);
}

/** The first `limit` blocks, re-joined, and whether anything was left behind.
    `clamped` is false when the source has `limit` blocks or fewer - a short
    comment reads exactly as it does today, with no fade and no control. */
export function clampBlocks(markdown: string, limit: number): { head: string; clamped: boolean } {
  const blocks = splitBlocks(markdown);
  if (blocks.length <= limit) return { head: markdown, clamped: false };
  return { head: blocks.slice(0, limit).join('\n\n'), clamped: true };
}
