import { Badge } from '@hatch/ui';
import type { IssueType } from '../types';
import { issueTypeRow } from '../lib/issueType';
import { TypeTile, type TileFill } from './TypeGlyph';

const FILL_WORDS: Record<TileFill, string> = {
  outline: 'Not started',
  solid: 'Under way',
  landed: 'Landed',
};

/* The type as its tile beside its word, on the page's own plain face. The
   colour is said once, by the tile; `.hatch-badge.hatch-type-badge` in App.css
   gives the face its border and ink, and overrides the Badge's tone.

   A list that mixes columns passes `fill`, and the tile says where the ticket
   stands; the glyph then carries a title saying the fill in words. The tick's
   ring is cut in the tile's own default ground, `--bg`, which is what a list's
   card sits on. Without `fill` the badge is what it always was. */
export function TypeBadge({ type, fill }: { type: IssueType; fill?: TileFill }) {
  const row = issueTypeRow(type);
  return (
    <Badge className={`hatch-type-badge ${row.class}`}>
      <span className="hatch-type-pill-glyph" aria-hidden="true" {...(fill ? { title: FILL_WORDS[fill] } : {})}>
        {fill ? <TypeTile type={type} fill={fill} /> : <TypeTile type={type} />}
      </span>
      {row.word}
    </Badge>
  );
}
