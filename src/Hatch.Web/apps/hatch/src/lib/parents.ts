/* What an issue may hang under.

   Read off LEGAL_PARENT_TYPES, which is what the server refuses by, and shared
   by the two screens that pick a parent - the issue page and the New issue
   dialog - so the candidates and the sentence describing them cannot part
   company. The server still decides; this only keeps a picker from offering
   what it will refuse.

   The cards come back in the order they were given: ordering belongs to the
   picker (pickerRows), which sorts by key and floats the best match. */

import { LEGAL_PARENT_TYPES } from '../types';
import type { IssueCard, IssueType } from '../types';

/**
 * The cards in `projectKey` an issue of `type` may hang under, in input order.
 * `self`, when given, is left out: an issue is never its own parent. A dialog
 * filing a new issue has no self to name.
 */
export const parentCandidates = (
  cards: IssueCard[],
  projectKey: string,
  type: IssueType,
  self?: string,
): IssueCard[] => {
  const legal = LEGAL_PARENT_TYPES[type];
  return cards.filter((c) => c.projectKey === projectKey && c.key !== self && legal.includes(c.type));
};

/* "an" before a vowel, as IssuesController's Article does - every issue type
   is one word, so the first letter is all there is to look at. */
const article = (type: IssueType): string => (/^[aeiou]/i.test(type) ? 'an' : 'a');

/**
 * The sentence naming what a `type` may hang under: "A task hangs under a
 * story, a bug or an epic." Two read "a or b" and one reads "a". Not the
 * server's refusal, which joins every pair with "or" and so reads "a story or
 * a bug or an epic" once there are three.
 */
export const parentHint = (type: IssueType): string => {
  const names = LEGAL_PARENT_TYPES[type].map((t) => `${article(t)} ${t}`);
  const list = names.length > 1 ? `${names.slice(0, -1).join(', ')} or ${names[names.length - 1]}` : names[0];
  return `${article(type) === 'an' ? 'An' : 'A'} ${type} hangs under ${list}.`;
};

/**
 * What an `IssuePicker` with no candidates says, for the New issue dialog's
 * Parent field - `projectKey` is undefined until a project is chosen, and
 * naming no project is what criterion 4 asks for rather than interpolating
 * `undefined` into the sentence.
 */
export const parentEmptyMessage = (projectKey: string | undefined, type: IssueType): string =>
  projectKey
    ? `Nothing in ${projectKey} can be a parent of a ${type} yet.`
    : 'Nothing can be a parent until a project is chosen.';
