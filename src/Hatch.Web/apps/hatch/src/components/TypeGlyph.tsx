import type { IssueType } from '../types';

const LABEL: Record<IssueType, string> = {
  epic: 'epic',
  reflection: 'reflection',
  story: 'story',
  task: 'task',
  bug: 'bug',
};

/**
 * The card's type, as a shape in the corner rather than a word in the body.
 *
 * Five distinct shapes - diamond, hexagon, circle, square, triangle - not five
 * colours of one `<rect>`, so the type still reads once colour is gone: greyscale print,
 * colour-blind vision, or a glance too quick to register hue. The colour is
 * still the fast read; App.css sets it per type, off `currentColor` here.
 *
 * `title` and `aria-label` both carry the word `TypeBadge` already rendered
 * verbatim - no new vocabulary to keep in sync with it.
 */
export function TypeGlyph({ type }: { type: IssueType }) {
  return (
    <span className={`hatch-type-glyph hatch-type-glyph--${type}`} title={LABEL[type]} aria-label={LABEL[type]}>
      <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
        {type === 'epic' && (
          <rect className="hatch-type-glyph-shape" x="3" y="3" width="10" height="10" transform="rotate(45 8 8)" />
        )}
        {type === 'reflection' && (
          <polygon className="hatch-type-glyph-shape" points="5,2 11,2 14,8 11,14 5,14 2,8" />
        )}
        {type === 'story' && <circle className="hatch-type-glyph-shape" cx="8" cy="8" r="6" />}
        {type === 'task' && (
          <rect className="hatch-type-glyph-shape" x="2" y="2" width="12" height="12" rx="1.5" />
        )}
        {type === 'bug' && <polygon className="hatch-type-glyph-shape" points="8,2 14,13 2,13" />}
      </svg>
    </span>
  );
}
