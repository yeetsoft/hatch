/* The local-model levels, as data: shared by the control that draws them, the
   Details card and the board card, so none of them copies another's table. */

import type { LocalModel } from '../types';

export type { LocalModel };

export interface LocalModelLevel {
  name: LocalModel;
  word: string;
  glyph: string;
  class: string;
  title: string;
}

/** Every level the picker offers, in the order the band draws them - this is
    the one place that names a level. */
export const LOCAL_MODEL_LEVELS: LocalModelLevel[] = [
  {
    name: 'none',
    word: 'None',
    glyph: '',
    class: '',
    title: 'Runs on the cloud model, as it always has.',
  },
  {
    name: 'prefer',
    word: 'Prefer',
    glyph: '◇',
    class: 'prefer',
    title: 'Uses a local model where one can do the work, and falls back to the cloud model where none can.',
  },
  {
    name: 'require',
    word: 'Require',
    glyph: '◆',
    class: 'require',
    title: 'Runs only on a local model, and never falls back to the cloud model quietly.',
  },
];

/** The level a name stands for, or none for a name this table does not know. */
export function localModelLevel(name: LocalModel): LocalModelLevel {
  return LOCAL_MODEL_LEVELS.find((level) => level.name === name) ?? LOCAL_MODEL_LEVELS[0];
}

/** The Details card's text: the level's word, and the ancestor it came from
    when it is not this issue's own. */
export function localModelFact(level: LocalModel, from: string | null): string {
  const { word } = localModelLevel(level);
  if (level === 'none') return word;
  return from ? `${word} (inherited from ${from})` : word;
}
