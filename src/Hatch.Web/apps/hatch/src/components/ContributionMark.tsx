import { originHref, originParts } from '../lib/contribution';
import type { ContributionRecord } from '../types';

/**
 * The mark an issue wears when it came from outside: an arrow pointing in at a
 * bracket, stroke only. A shape and not a colour, so it reads in a monochrome
 * render - and none of the five type glyphs (diamond, hexagon, circle, square,
 * triangle) is open-stroked, so it is never mistaken for one. It sits beside
 * the type glyph and does not replace it: a mirrored epic is an epic and a
 * contribution at once.
 *
 * Everything is `currentColor`; the colour comes from App.css.
 */
export function ContributionMark({ className = '' }: { className?: string }) {
  return (
    <span
      className={`hatch-contribution-mark${className ? ` ${className}` : ''}`}
      title="Contribution - came from outside"
      aria-label="contribution"
    >
      <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
        <path
          className="hatch-contribution-mark-shape"
          d="M1.5 8H9.5M6.5 4.5 10 8l-3.5 3.5M11.5 2.5h3v11h-3"
        />
      </svg>
    </span>
  );
}

/**
 * Where a contribution came from: the originating key and, where the record has
 * one, the instance's host - else `@login`. The instance is a link only where
 * the record's instance is an http(s) address, and the link is text and href
 * alone: nothing here fetches it.
 *
 * `inLink` is for a badge drawn inside another link - the board card is one,
 * and a nested `<a>` is never valid markup. There the instance is a
 * `role="link"` span that opens the address in a new tab, the way ClaimBadge's
 * runner head does for its own page.
 */
export function ContributionBadge({
  contribution,
  inLink = false,
  className = '',
}: {
  contribution: ContributionRecord;
  inLink?: boolean;
  className?: string;
}) {
  const { name, host } = originParts(contribution);
  const href = originHref(contribution);

  return (
    <span className={`hatch-contribution-badge${className ? ` ${className}` : ''}`}>
      <span className="hatch-contribution-origin">{name}</span>
      {host && (
        <>
          {' · '}
          {href && !inLink && (
            <a href={href} target="_blank" rel="noopener noreferrer" className="hatch-contribution-instance">
              {host}
            </a>
          )}
          {href && inLink && (
            <span
              className="hatch-contribution-instance"
              role="link"
              tabIndex={0}
              title={href}
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                window.open(href, '_blank', 'noopener,noreferrer');
              }}
              onKeyDown={(e) => {
                if (e.key !== 'Enter') return;
                e.preventDefault();
                e.stopPropagation();
                window.open(href, '_blank', 'noopener,noreferrer');
              }}
              onMouseDown={(e) => e.stopPropagation()}
              onTouchStart={(e) => e.stopPropagation()}
            >
              {host}
            </span>
          )}
          {!href && <span className="hatch-contribution-instance">{host}</span>}
        </>
      )}
    </span>
  );
}
