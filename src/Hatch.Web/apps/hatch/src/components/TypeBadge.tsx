import { Badge } from '@hatch/ui';
import type { IssueType } from '../types';
import { issueTypeRow } from '../lib/issueType';

/* The type's own face, the same one the control's pill and the board card's
   glyph wear: `.hatch-type-badge.<class>` in App.css gives it the colour, and
   overrides the Badge's tone outright. */
export function TypeBadge({ type }: { type: IssueType }) {
  const row = issueTypeRow(type);
  return <Badge className={`hatch-type-badge ${row.class}`}>{row.word}</Badge>;
}
