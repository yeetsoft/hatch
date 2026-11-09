import { Link } from 'react-router-dom';
import { foldRows, segmentStyle, segmentTitle, ticks, todayX, xOf, type Scale } from '../lib/timeline';
import type { Status, Timeline } from '../types';
import { TypeGlyph } from './TypeGlyph';

/** The narrowest a segment is drawn, in the lane's own units, so one that began
    and ended inside the same minute is still a mark rather than nothing. */
const MIN_WIDTH = 0.2;

/** The indent a label is padded by, as a custom property for CSS to multiply. */
const depthVar = (depth: number): Record<string, string> => ({ '--depth': String(depth) });

interface Props {
  timeline: Timeline;
  statuses: Status[];
  scale: Scale;
  offsetMinutes: number;
  now: Date;
  /** The keys of the rows that are open; the page owns it. */
  expanded: ReadonlySet<string>;
  onToggle: (key: string) => void;
  /** The top-level keys left out of the chart; the page owns it. */
  hidden: ReadonlySet<string>;
  onHide: (key: string) => void;
  /** For the tests to pin; the page passes none and gets the reader's. */
  locale?: string;
}

/**
 * When the work happened: a label column beside a pane of lanes, one row each.
 *
 * Hand-cut SVG in the house's own tokens, the way `SpendGraph` is. The label
 * column does not scroll and the pane does, so on a phone the names stay put
 * while the chart slides under them. Which rows exist, where a tick falls and
 * where now sits are `lib/timeline.ts`'s answers; this places them.
 *
 * The lanes and the labels line up by sharing one row height in CSS, not by
 * measuring anything.
 */
export function TimelineChart({ timeline, statuses, scale, offsetMinutes, now, expanded, onToggle, hidden, onHide, locale }: Props) {
  const rows = foldRows(timeline, expanded, hidden);
  const today = todayX(scale, now);
  const statusName = (id: number) => statuses.find((status) => status.id === id)?.name ?? 'Unknown';

  return (
    <div className="hatch-timeline">
      <div className="hatch-timeline-labels">
        <div className="hatch-timeline-corner" aria-hidden />
        {rows.map((row) => {
          const { issue } = row.entry;
          const name = row.isOther ? 'Other' : issue.key;

          return (
            <div key={issue.key} className="hatch-timeline-label" style={depthVar(row.depth)}>
              <TypeGlyph type={issue.type} />
              {!row.isOther && (
                <Link to={`/issues/${issue.key}`} className="hatch-timeline-key">
                  {issue.key}
                </Link>
              )}
              <span className="hatch-timeline-title">{issue.title}</span>
              {row.expandable && (
                <button
                  type="button"
                  className="hatch-timeline-toggle"
                  aria-expanded={row.expanded}
                  aria-label={`Children of ${name}`}
                  title={`Children of ${name}`}
                  onClick={() => onToggle(issue.key)}
                >
                  {row.expanded ? '−' : '+'}
                </button>
              )}
              {row.depth === 0 && (
                <button
                  type="button"
                  className="hatch-timeline-hide"
                  aria-label={`Hide ${name}`}
                  onClick={() => onHide(issue.key)}
                >
                  Hide
                </button>
              )}
            </div>
          );
        })}
      </div>
      <div className="hatch-timeline-pane">
        <div className="hatch-timeline-track">
          <div className="hatch-timeline-ruler">
            {ticks(scale, offsetMinutes, locale).map((tick) => (
              <span key={tick.x} className="hatch-timeline-tick" style={{ left: `${tick.x}%` }}>
                {tick.label}
              </span>
            ))}
          </div>
          {rows.map((row) => {
            const { issue, segments } = row.entry;

            return (
              <div key={issue.key} className="hatch-timeline-lane">
                <svg
                  className="hatch-timeline-svg"
                  viewBox="0 0 100 10"
                  preserveAspectRatio="none"
                  role="img"
                  aria-label={row.isOther ? 'Other' : `${issue.key} ${issue.title}`}
                >
                  {segments.map((segment) => {
                    const x = xOf(scale, segment.from);

                    return (
                      <rect
                        key={segment.from}
                        className={`hatch-timeline-segment${row.isOther ? ' hatch-timeline-segment--other' : ''}`}
                        x={x}
                        y={2}
                        height={6}
                        width={Math.max(xOf(scale, segment.to ?? scale.to) - x, MIN_WIDTH)}
                        style={segmentStyle(statuses, segment, row.isOther) ?? undefined}
                      >
                        <title>{segmentTitle(statusName(segment.statusId), segment, locale)}</title>
                      </rect>
                    );
                  })}
                </svg>
              </div>
            );
          })}
          {today !== null && <div className="hatch-timeline-today" style={{ left: `${today}%` }} aria-hidden />}
        </div>
      </div>
    </div>
  );
}
