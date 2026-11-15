export type Priority = 'normal' | 'expedited' | 'emergency' | 'low' | 'economy' | 'paused';

export interface PriorityLevel {
  name: Priority;
  word: string;
  glyph: string;
  class: string;
  title: string;
}

/** Every level the picker offers, in the order the band draws them - this is
    the one place that names a level. */
export const PRIORITY_LEVELS: PriorityLevel[] = [
  {
    name: 'emergency',
    word: 'Emergency',
    glyph: '🚨',
    class: 'emergency',
    title:
      'The highest priority: goes first, above everything expedited, and the dispatcher considers it before anything else.',
  },
  {
    name: 'expedited',
    word: 'Expedited',
    glyph: '↑',
    class: 'expedited',
    title: 'This one goes first: it floats to the top of its column and the dispatcher reaches for it before anything else.',
  },
  {
    name: 'normal',
    word: 'Normal',
    glyph: '',
    class: '',
    title: 'No float: this one waits its turn in the column, in the usual order.',
  },
  {
    name: 'low',
    word: 'Low',
    glyph: '↓',
    class: 'low',
    title: 'Low — reached after everything normal, and only while the session window is on pace.',
  },
  {
    name: 'economy',
    word: 'Economy',
    glyph: '🌙',
    class: 'economy',
    title:
      'Worked only with usage that would otherwise go spare - the dispatcher reaches for it last, and only when the account is ahead of pace.',
  },
  {
    name: 'paused',
    word: 'Paused',
    glyph: '⏸',
    class: 'paused',
    title: 'Paused: a person set this aside; the loop leaves it where it stands until they set it back.',
  },
];


/** Where the keyboard lands when the priority band opens on the issue's own
    level: one entry forward in the band's fixed order, wrapping to the first
    entry past the end - since the own-level button is disabled the same way
    `columns.ts`'s current column is, and there is always a next entry to land
    on rather than a last one to back away from.

    Pure and generic rather than importing `PriorityControl`'s own level list,
    so it is unit-testable without a render the way `landingFocus` is for
    columns - see `lib/columns.ts`. */
export function priorityLandingFocus<T extends { name: string }>(
  levels: readonly T[],
  ownName: string,
): T['name'] | null {
  const at = levels.findIndex((level) => level.name === ownName);
  if (levels.length === 0) return null;
  if (at < 0) return levels[0].name;
  return levels[(at + 1) % levels.length].name;
}
