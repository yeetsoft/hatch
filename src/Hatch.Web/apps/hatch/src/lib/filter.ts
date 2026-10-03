/* The board's filter: which types to draw, and a search box.

   Deliberately dumb, and dumb in a stated way - the board already holds every
   issue in the house in the browser (BoardDto is one request, unpaged), so this
   is a pass over an array rather than a round trip, and it can afford to be
   obvious. Anything cleverer - fuzzy matching, ranking, stemming - would make
   "why is that card not showing" a question with no answer.

   Filtering is the browser's, not the server's, on purpose: the filter is a
   view of the board, and a board that refetched on every keystroke would drop
   the drag it was in the middle of. */

import { UNASSIGNED, assigneeToken, compareAssignees } from './assignee';
import { ISSUE_TYPES } from '../types';
import type { Assignee, IssueCard, IssueType } from '../types';

export interface CardFilter {
  /** The types to draw, in `ISSUE_TYPES` order. Never empty: the last one
      drawn cannot be switched off (`toggleType`), so a board never goes blank
      from ticking boxes, and there is no "empty means all" to tell apart from
      "empty means none". */
  types: IssueType[];
  /** What was typed in the search box, raw. */
  query: string;
  /** Only the cards holding a question nobody has answered.
      Its own switch rather than a search term, because "what is waiting on me"
      is the question an operator opens the board with when the loop has stopped
      moving, and it should not depend on remembering a word to type. */
  waiting: boolean;
  /** Whose cards to draw: `''` is everybody's, `UNASSIGNED` is the ones nobody
      owns, and anything else is one identity's token (lib/assignee.ts).

      A string rather than an `Assignee | null | undefined`, because a <select>
      holds a string and the three states have to be told apart - and "nobody"
      is a choice somebody makes, not the absence of one. */
  assignee: string;
  /** Which project's cards to draw, by key - `''` is every project's. Lives in
      the URL rather than local state (see BoardFilters), so the half of the
      filter that answers "whose board is this" survives a reload. */
  project: string;
}

/** What the board opens on, and what Reset returns to: every type, every
    column saying what it holds. */
export const DEFAULT_FILTER: CardFilter = { types: [...ISSUE_TYPES], query: '', waiting: false, assignee: '', project: '' };

/** Whether this filter is hiding anything, which is what decides if the board
    says so out loud, and if Reset is drawn. */
export const isFiltering = (filter: CardFilter): boolean =>
  filter.types.length < ISSUE_TYPES.length ||
  filter.query.trim() !== '' ||
  filter.waiting ||
  filter.assignee !== '' ||
  filter.project !== '';

/**
 * Everything about a card that a search box can see: its key, its title, its
 * type, and the parent it hangs under. Not its description - the board does not
 * carry one (IssueCardDto), and searching text the browser does not have would
 * find nothing while looking like it worked.
 */
const haystack = (card: IssueCard): string =>
  `${card.key} ${card.title} ${card.type} ${card.parentKey ?? ''}`.toLowerCase();

/**
 * Whether a card matches what was typed. Every whitespace-separated term has to
 * appear somewhere, in any order - so "bug cert" finds the certificate bug
 * without anybody having to type the words the way the title has them.
 */
export function matchesQuery(card: IssueCard, query: string): boolean {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return true;

  const text = haystack(card);
  return terms.every((term) => text.includes(term));
}

/**
 * Whether a card belongs to whoever was chosen. Off matches everything;
 * `UNASSIGNED` is the cards with no assignee at all,
 * which includes the ones whose person has been deleted or whose key has been
 * revoked - the server has already resolved those to null, so there is nothing
 * for the browser to know about it.
 */
export const matchesAssignee = (card: IssueCard, assignee: string): boolean => {
  if (assignee === '') return true;
  if (assignee === UNASSIGNED) return card.assignee === null;
  return card.assignee !== null && assigneeToken(card.assignee) === assignee;
};

export const matchesFilter = (card: IssueCard, filter: CardFilter): boolean =>
  filter.types.includes(card.type) &&
  (!filter.waiting || card.openQuestions > 0) &&
  matchesAssignee(card, filter.assignee) &&
  (filter.project === '' || card.projectKey === filter.project) &&
  matchesQuery(card, filter.query);

export const filterCards = (cards: IssueCard[], filter: CardFilter): IssueCard[] =>
  cards.filter((card) => matchesFilter(card, filter));

/**
 * The assignees the board actually has cards for, in picker order and with
 * duplicates collapsed.
 *
 * Derived from the cards rather than fetched, for the reason this whole module
 * gives: the board is already in the browser, and a facet offering somebody
 * with no cards on it is a choice that can only blank the screen. The picker on
 * the issue page is the other question - who *could* own this - and that one is
 * a read.
 */
export function assigneeFacets(cards: IssueCard[]): Assignee[] {
  const seen = new Map<string, Assignee>();
  for (const card of cards) {
    if (card.assignee) seen.set(assigneeToken(card.assignee), card.assignee);
  }

  return [...seen.values()].sort(compareAssignees);
}

/** Flips the waiting switch. */
export const toggleWaiting = (filter: CardFilter): CardFilter => ({ ...filter, waiting: !filter.waiting });

/** Adds or removes one type, which is what a list of checkboxes does to a
    filter. Unticking the only one left returns the filter itself, unchanged. */
export function toggleType(filter: CardFilter, type: IssueType): CardFilter {
  const next = filter.types.includes(type) ? filter.types.filter((t) => t !== type) : [...filter.types, type];
  if (next.length === 0) return filter;
  return { ...filter, types: ISSUE_TYPES.filter((t) => next.includes(t)) };
}

/** Draws a type if it is not drawn already. Filing an issue of a hidden type
    uses it, so the card is on the board it was filed from. Returns the same
    object when there is nothing to do, so a caller can skip a re-render. */
export const revealType = (filter: CardFilter, type: IssueType): CardFilter =>
  filter.types.includes(type)
    ? filter
    : { ...filter, types: ISSUE_TYPES.filter((t) => t === type || filter.types.includes(t)) };

/** How many cards of each type there are, zero included, so a row can say "0"
    rather than nothing. Given the whole board and not the visible cards, for
    the reason `assigneeFacets` is: choosing a thing must not shrink the list
    you chose it from. */
export function typeCounts(cards: IssueCard[]): Record<IssueType, number> {
  const counts: Record<IssueType, number> = { epic: 0, story: 0, task: 0, bug: 0 };
  for (const card of cards) counts[card.type] += 1;
  return counts;
}

const PLURALS: Record<IssueType, string> = { epic: 'epics', story: 'stories', task: 'tasks', bug: 'bugs' };

/** What the Types control says: `All`, `All but tasks` for three, and for one
    or two the ones drawn (`Epics, stories`). */
export function typesSummary(types: IssueType[]): string {
  if (types.length >= ISSUE_TYPES.length) return 'All';
  if (types.length === ISSUE_TYPES.length - 1) {
    const missing = ISSUE_TYPES.find((type) => !types.includes(type));
    if (missing) return `All but ${PLURALS[missing]}`;
  }

  const listed = ISSUE_TYPES.filter((type) => types.includes(type))
    .map((type) => PLURALS[type])
    .join(', ');
  return listed.charAt(0).toUpperCase() + listed.slice(1);
}
