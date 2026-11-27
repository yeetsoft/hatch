/* What an issue may hang under.

   Read off LEGAL_PARENT_TYPES, which is what the server refuses by, and shared
   by the two screens that pick a parent - the issue page and the New issue
   dialog - so the candidates and the sentence describing them cannot part
   company. The server still decides; this only keeps a picker from offering
   what it will refuse.

   The list spans every project the reader can see. The cards come back with
   the issue's own project first, then the other projects A to Z, each by key
   number - sorted here, not left to the picker, so the order is tested where
   it is made. */

import { LEGAL_PARENT_TYPES } from '../types';
import { compareIssueKeys } from './issuePicker';
import type { PickedRow } from './pickerLookup';
import type { IssueCard, IssueType } from '../types';

/**
 * The cards an issue of `type` may hang under, from any project: `projectKey`'s
 * own first, the other projects A to Z, and inside each by key number.
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
  return cards
    .filter((c) => c.key !== self && legal.includes(c.type))
    .sort((a, b) => compareIssueKeys(a.key, b.key, projectKey));
};

/* "an" before a vowel, as IssuesController's Article does - every issue type
   is one word, so the first letter is all there is to look at. */
const article = (type: IssueType): string => (/^[aeiou]/i.test(type) ? 'an' : 'a');

/**
 * The sentence naming what a `type` may hang under: "A task hangs under a
 * story, a bug or an epic, in any project." Two read "a or b" and one reads "a". Not the
 * server's refusal, which joins every pair with "or" and so reads "a story or
 * a bug or an epic" once there are three.
 */
export const parentHint = (type: IssueType): string => {
  if (LEGAL_PARENT_TYPES[type].length === 0) return `${article(type) === 'an' ? 'An' : 'A'} ${type} takes no parent.`;
  const names = LEGAL_PARENT_TYPES[type].map((t) => `${article(t)} ${t}`);
  const list = names.length > 1 ? `${names.slice(0, -1).join(', ')} or ${names[names.length - 1]}` : names[0];
  return `${article(type) === 'an' ? 'An' : 'A'} ${type} hangs under ${list}, in any project.`;
};

/**
 * What an `IssuePicker` with no candidates says, for the New issue dialog's
 * Parent field - `projectKey` is undefined until a project is chosen. Once
 * one is, the sentence invites typing rather than naming none: a lookup can
 * still find a legal parent closed long before the board read it, so
 * "nothing can be a parent yet" would be wrong as soon as it is typed about.
 */
export const parentEmptyMessage = (projectKey: string | undefined, type: IssueType): string =>
  projectKey
    ? `Type a key, a number or title words to find a parent for ${article(type)} ${type}.`
    : 'Nothing can be a parent until a project is chosen.';

/**
 * The same refusal `parentCandidates` applies to the board's own cards, over
 * a row the lookup found: a legal type, and never `self`. A relative among
 * the page's own cards is caught by `parentCandidates` already; one closed
 * long enough to be off the board is not, and only this and the server's own
 * refusal catch it.
 */
export function parentLookupFilter(type: IssueType, self?: string): (row: PickedRow) => boolean {
  const legal = LEGAL_PARENT_TYPES[type];
  return (row) => row.key !== self && legal.some((t) => t === row.type);
}

/**
 * `picked` when it is still a legal parent for `type` in a chosen project,
 * else null - the New issue dialog's own clamp, read off a looked-up row
 * rather than a card list. A pick that no longer qualifies (the type was
 * changed, say) is not forgotten: `picked` is left as the caller holds it,
 * so the pick reappears the moment the type allows it again.
 */
export function parentInForce(
  picked: PickedRow | null,
  projectKey: string | undefined,
  type: IssueType,
): PickedRow | null {
  return picked && projectKey !== undefined && parentLookupFilter(type)(picked) ? picked : null;
}
