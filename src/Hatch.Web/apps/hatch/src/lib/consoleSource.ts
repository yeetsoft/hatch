import type { IssueCard, Status } from '../types';

/** The pieces of a host or a page's moves that say what the console may offer. */
interface Hosted {
  cards: IssueCard[];
  statuses: Status[];
}
interface Held {
  card?: IssueCard | null;
  statuses?: Status[];
}

/**
 * The cards and columns the console starts from, with no read of its own.
 *
 * The host's win: the board holds every issue and every column. Otherwise the
 * page's own issue and columns, if it handed any - the issue page does, and so
 * `>status` works there even for an issue the board no longer shows. Otherwise
 * nothing: the rows come from the projects' numbers and the server's lookup.
 */
export function consoleSource(
  host: Hosted | null,
  moves: Held | null,
): { cards: IssueCard[]; statuses: Status[] } {
  if (host) return { cards: host.cards, statuses: host.statuses };
  return { cards: moves?.card ? [moves.card] : [], statuses: moves?.statuses ?? [] };
}
