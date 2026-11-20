import type { Me } from '../types';
import { message } from './errors';

/**
 * The rules behind the Home screen card on the Settings page: which home
 * screens there are, who the card is drawn for, and what pressing a radio
 * does.
 *
 * Pulled out of the component the way lib/hatchSettings.ts pulls the
 * credential and name rules out, so the press-a-radio behaviour can be
 * tested without a DOM runner - this repository has none.
 */

/** The two home screens a person can choose between. */
export type Home = NonNullable<Me['home']>;

export interface HomeChoice {
  value: Home;
  label: string;
}

/** What the card offers, Board first since it is the default. */
export const HOME_CHOICES: HomeChoice[] = [
  { value: 'board', label: 'Board' },
  { value: 'ambitions', label: 'Ambitions' },
];

/**
 * Whether the card is drawn at all. Only `person` and `local` have a row to
 * write the choice to - `none` is a browser nobody has signed into, and
 * `key` is a program, never a browser reading this page. `null` is the first
 * read not having answered yet, same as those two.
 */
export const homeCardApplies = (me: Me | null): boolean => me?.kind === 'person' || me?.kind === 'local';

/** The checked choice: what is saved, or Board when nothing is. */
export const homeOf = (me: Me | null): Home => me?.home ?? 'board';

/** What choosing a home screen sends and what it does once answered. */
export async function chooseHome(
  home: Home,
  { put, announce }: { put: (home: Home) => Promise<Me>; announce: () => void },
): Promise<{ saved: Home } | { error: string }> {
  try {
    const answer = await put(home);
    announce();
    return { saved: answer.home ?? home };
  } catch (err) {
    return { error: message(err) };
  }
}
