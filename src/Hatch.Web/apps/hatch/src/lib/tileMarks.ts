import type { IssueCard } from '../types';
import type { PreApproval } from './preApproval';

/* What a type's tile wears besides its picture: a trail for pre-approval, a
   flame for priority. One table here, so the component that draws the marks and
   the test that pins them read the same rows. The fill is not here - it is
   where a ticket stands, not a level a card carries.

   All of it is drawn in the 16 box the tile keeps, or around it in the wider one. */

export type Priority = IssueCard['priority'];

/** The box a bare tile draws in. */
export const TILE_BOX = '0 0 16 16';

/** The box a tile with a trail or a flame draws in: 12 to the left of the tile and 8 above it, 29 wide and 25 tall. */
export const WIDE_BOX = '-12 -8 29 25';

export interface Trail {
  /** Path data for the lines, left of the tile. Empty for none. */
  lines: string;
  /** The tile leans forward. */
  lean: boolean;
}

/** Lines left of the tile, one more of them for each step of pre-approval. */
export const TRAILS: Record<PreApproval, Trail> = {
  none: { lines: '', lean: false },
  express: { lines: 'M-7.6 8h5.2', lean: false },
  preapproved: { lines: 'M-6.3 4.6h3.6 M-9 8h6.6 M-5.8 11.4h3.2', lean: true },
};

/** What the tile's face and picture are put through when it leans. */
export const LEAN = 'translate(1.5 0) skewX(-10)';

/** How much of the flame a priority draws. `dim` draws none, and fades the tile instead. */
export type FlameKind = 'none' | 'dim' | 'ember' | 'upright';

export interface Heat {
  flame: FlameKind;
  /** Puts the flame's 24 box, base centred on (12, 22), where the tile wants it. Empty where there is no flame. */
  transform: string;
}

export const HEATS: Record<Priority, Heat> = {
  paused: { flame: 'none', transform: '' },
  low: { flame: 'none', transform: '' },
  normal: { flame: 'none', transform: '' },
  economy: { flame: 'dim', transform: '' },
  expedited: { flame: 'ember', transform: 'translate(8 9) scale(0.7) translate(-12 -22)' },
  emergency: { flame: 'upright', transform: 'translate(8 10.5) scale(0.95) translate(-12 -22)' },
};

/** The pre-approved emergency: the flame streams back off the leaning tile, over two lines in place of three. */
export const STREAMING = {
  transform: 'translate(7 9) rotate(-74) scale(0.9) translate(-12 -22)',
  lines: 'M-9.5 12.6h6.4 M-6.4 15.2h3.6',
};

/** The opacity of a tile whose ticket can wait. */
export const DIM_OPACITY = 0.4;

export interface TileMarks {
  /** Path data for the trail lines, or empty. */
  trail: string;
  lean: boolean;
  /** A flame or an ember is drawn behind the tile. */
  flame: boolean;
  flameTransform: string;
  dim: boolean;
  /** The tile needs the wider box. */
  wide: boolean;
}

export function tileMarks(preApproval: PreApproval, priority: Priority): TileMarks {
  const heat = HEATS[priority];
  const trail = TRAILS[preApproval];
  const streaming = priority === 'emergency' && preApproval === 'preapproved';
  const flame = heat.flame === 'ember' || heat.flame === 'upright';
  const lines = streaming ? STREAMING.lines : trail.lines;
  return {
    trail: lines,
    lean: trail.lean,
    flame,
    flameTransform: streaming ? STREAMING.transform : heat.transform,
    dim: heat.flame === 'dim',
    wide: lines !== '' || flame,
  };
}
