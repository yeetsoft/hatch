import { Badge } from '@hatch/ui';
import type { IssueType } from '../types';
import { issueTypeRow } from '../lib/issueType';
import { TypeTile } from './TypeGlyph';

/* The type as its tile beside its word, on the page's own plain face. The
   colour is said once, by the tile; `.hatch-badge.hatch-type-badge` in App.css
   gives the face its border and ink, and overrides the Badge's tone. */
export function TypeBadge({ type }: { type: IssueType }) {
  const row = issueTypeRow(type);
  return (
    <Badge className={`hatch-type-badge ${row.class}`}>
      <span className="hatch-type-pill-glyph" aria-hidden="true">
        <TypeTile type={type} />
      </span>
      {row.word}
    </Badge>
  );
}
