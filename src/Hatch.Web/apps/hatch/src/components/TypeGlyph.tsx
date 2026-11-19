import { useId } from 'react';
import { HATCH_MARK_BODY, HATCH_MARK_CAP } from '@hatch/ui';
import type { IssueType } from '../types';
import { issueTypeRow } from '../lib/issueType';
import type { TypePicture } from '../lib/issueType';

const LABEL: Record<IssueType, string> = {
  ambition: 'ambition',
  epic: 'epic',
  reflection: 'reflection',
  story: 'story',
  task: 'task',
  bug: 'bug',
};

/** A rounded square, 14 units across, in the 16 by 16 box. */
const ROUNDED_FACE =
  'M4.5 1h7A3.5 3.5 0 0 1 15 4.5v7a3.5 3.5 0 0 1-3.5 3.5h-7A3.5 3.5 0 0 1 1 11.5v-7A3.5 3.5 0 0 1 4.5 1Z';

interface Picture {
  filled?: string;
  stroked?: { d: string; width: number };
}

/** What each tile pictures, drawn in the 16 by 16 box before the 110% scale. */
const PICTURES: Record<Exclude<TypePicture, 'egg'>, Picture> = {
  layers: {
    filled: 'M8 3.4 12.6 5.9 8 8.4 3.4 5.9Z',
    stroked: { d: 'M3.7 8.4 8 10.7 12.3 8.4 M3.7 10.7 8 13 12.3 10.7', width: 1.3 },
  },
  bookmark: {
    filled: 'M5.2 3.6h5.6a.6.6 0 0 1 .6.6v8.4L8 10.3l-3.4 2.3V4.2a.6.6 0 0 1 .6-.6Z',
  },
  tick: {
    stroked: { d: 'M4.6 8.4 7 10.8 11.5 5.6', width: 1.9 },
  },
  beetle: {
    filled: 'M8 5.4c1.7 0 2.9 1.4 2.9 3.5 0 2.1-1.2 3.7-2.9 3.7s-2.9-1.6-2.9-3.7c0-2.1 1.2-3.5 2.9-3.5Z M6.5 4.7a1.5 1.5 0 0 1 3 0Z',
    stroked: {
      d: 'M5.3 7.4 3.6 6.5 M5.1 9.2H3.3 M5.5 11 3.9 12.1 M10.7 7.4 12.4 6.5 M10.9 9.2h1.8 M10.5 11 12.1 12.1',
      width: 1.1,
    },
  },
  sun: {
    filled:
      'M3.64 8.6A4.4 4.4 0 1 1 12.36 8.6Z M3.86 9.5h8.28A4.4 4.4 0 0 1 11.47 10.7H4.53A4.4 4.4 0 0 1 3.86 9.5Z M5.47 11.6h5.06A4.4 4.4 0 0 1 5.47 11.6Z',
  },
};

/**
 * The type's tile: a coloured outline with a picture on it. What the card's
 * corner glyph and the type pill both draw.
 *
 * The type reads by its picture and its outline - a rounded square for epic,
 * story and task, a disc for bug and reflection - so it still reads once
 * colour is gone: greyscale print, colour-blind vision, or a glance too quick
 * to register hue. The colour is still the fast read: the outline is filled
 * with the type's own token, and the picture is `--on-accent` on it.
 *
 * The fill is set as a style because `var()` in a presentation attribute is
 * not trusted everywhere.
 */
export function TypeTile({ type }: { type: IssueType }) {
  const { outline, picture } = issueTypeRow(type);
  if (picture === 'egg') return <AmbitionTile />;
  const { filled, stroked } = PICTURES[picture];
  const fill = { fill: `var(--type-${type})` };
  return (
    <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
      {outline === 'disc' ? (
        <circle className="hatch-type-tile-face" cx="8" cy="8" r="7" style={fill} />
      ) : (
        <path className="hatch-type-tile-face" d={ROUNDED_FACE} style={fill} />
      )}
      <g transform="translate(8 8) scale(1.1) translate(-8 -8)">
        {filled && <path className="hatch-type-tile-picture" d={filled} />}
        {stroked && <path className="hatch-type-tile-stroke" d={stroked.d} strokeWidth={stroked.width} />}
      </g>
    </svg>
  );
}

/* The egg is the mark's 100 box at 0.119, so its 91-unit silhouette (the cap's
   apex at y 5 to the body's foot at 96) is about 11 units tall in the 16 box.
   It is centred on the silhouette, cap included, as hatchIconSvg centres it:
   the centre is (50, 50.5), and that point lands on (8, 8). */
const EGG_SCALE = 0.119;
const EGG_TX = (8 - 50 * EGG_SCALE).toFixed(4);
const EGG_TY = (8 - 50.5 * EGG_SCALE).toFixed(4);

/**
 * The ambition's tile: the Hatch egg, `--on-accent`, on a rounded square
 * filled with the brand gradient. The egg is the mark's own two paths from
 * `@hatch/ui`, never a copy.
 *
 * It takes no `type` and is not keyed by a row in `issueType.ts`: its fill is
 * a gradient between `--gradient-ink-from` and `--gradient-ink-to`, where
 * every other tile's is one `--type-*` token. The egg is already at its final
 * size, so it takes no further scaling.
 *
 * The gradient's id comes from `useId`, as HatchMark's does, so two tiles on
 * one page never share a def. The stops and the fill are styles because
 * `var()` in a presentation attribute is not trusted everywhere.
 */
export function AmbitionTile() {
  const gradientId = `hatch-ambition-tile-${useId().replace(/:/g, '')}`;
  return (
    <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id={gradientId} gradientUnits="userSpaceOnUse" x1="1" y1="1" x2="15" y2="15">
          <stop offset="0" style={{ stopColor: 'var(--gradient-ink-from)' }} />
          <stop offset="1" style={{ stopColor: 'var(--gradient-ink-to)' }} />
        </linearGradient>
      </defs>
      <path className="hatch-type-tile-face" d={ROUNDED_FACE} style={{ fill: `url(#${gradientId})` }} />
      <g transform={`translate(${EGG_TX} ${EGG_TY}) scale(${EGG_SCALE})`}>
        <path className="hatch-type-tile-picture" d={HATCH_MARK_CAP} />
        <path className="hatch-type-tile-picture" d={HATCH_MARK_BODY} />
      </g>
    </svg>
  );
}

/**
 * The card's type, as a tile in the corner rather than a word in the body.
 *
 * `title` and `aria-label` both carry the word `TypeBadge` already rendered
 * verbatim - no new vocabulary to keep in sync with it.
 */
export function TypeGlyph({ type }: { type: IssueType }) {
  return (
    <span className={`hatch-type-glyph hatch-type-glyph--${type}`} title={LABEL[type]} aria-label={LABEL[type]}>
      <TypeTile type={type} />
    </span>
  );
}
