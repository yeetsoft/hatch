/* The human-work values, as data: shared by the control that draws them, the
   Details card and the board card, so none of them copies another's table. */

import type { HumanWork } from '../types';

export type { HumanWork };

export interface HumanWorkValue {
  name: HumanWork;
  word: string;
  glyph: string;
  class: string;
  title: string;
}

/** Every value the band offers, in the order it draws them - this is the one
    place that names a value. */
export const HUMAN_WORK_VALUES: HumanWorkValue[] = [
  {
    name: 'inherit',
    word: 'Inherit',
    glyph: '',
    class: '',
    title: "Takes the nearest ancestor's own value, or agent with nothing set.",
  },
  {
    name: 'human',
    word: 'Human',
    glyph: '◆',
    class: 'human',
    title: 'A person does this one, and no runner picks it up.',
  },
  {
    name: 'agent',
    word: 'Agent',
    glyph: '',
    class: '',
    title: "The loop's to pick up, even under a human ancestor.",
  },
];

/** The value a name stands for, or inherit for a name this table does not know. */
export function humanWorkValue(name: HumanWork): HumanWorkValue {
  return HUMAN_WORK_VALUES.find((value) => value.name === name) ?? HUMAN_WORK_VALUES[0];
}

/** The Details card's text: the effective word, and the ancestor it came from
    when it is inherited. */
export function humanWorkFact(effective: HumanWork, from: string | null): string {
  const { word } = humanWorkValue(effective);
  return from ? `${word} (inherited from ${from})` : word;
}
