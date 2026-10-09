/* The three-block preview for a description or a comment (HA-332). `limit`
   null or undefined skips the clamp entirely - the board peek's own render of
   this markup keeps working unclamped, and is the reason this component takes
   `limit` rather than always clamping to a fixed count. */

import { useState } from 'react';
import { Button } from '@hatch/ui';
import { clampBlocks } from '../lib/prose';
import { renderMarkdown } from '../lib/markdown';

export function ProseClamp({
  source,
  limit,
  className,
}: {
  /** The raw markdown, as stored. */
  source: string;
  /** How many top-level blocks to show before fading and offering More.
      `null` or left off renders the whole source, unclamped. */
  limit?: number | null;
  /** Appended to the rendered markdown's own class - `hatch-question-body`,
      for a question's body, the only call site that needs it. */
  className?: string;
}) {
  const [expanded, setExpanded] = useState(false);
  const { head, clamped } = limit != null ? clampBlocks(source, limit) : { head: source, clamped: false };
  const showingHead = clamped && !expanded;

  const markup = (
    <div
      className={`hatch-markdown${className ? ` ${className}` : ''}`}
      dangerouslySetInnerHTML={{ __html: renderMarkdown(showingHead ? head : source) }}
    />
  );

  if (!clamped) return markup;

  return (
    <>
      <div className={`hatch-prose-clamp${showingHead ? ' hatch-prose-clamp--faded' : ''}`}>{markup}</div>
      <Button onClick={() => setExpanded(!expanded)}>{showingHead ? 'More' : 'Less'}</Button>
    </>
  );
}
