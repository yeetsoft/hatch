/* What the board says about an issue that came from outside: where it came
   from, and which of its ancestors did too. Read off the record the server
   stored (ContributionDto) and nothing else - the browser derives no origin
   of its own, and never fetches the one the record names. */

import type { ContributionRecord, IssueCard } from '../types';

/** The origin instance's host, or the raw string where it does not parse - the
    record is whatever an envelope said, and a label has to draw something. */
const hostOf = (instance: string): string => {
  try {
    return new URL(instance).host || instance;
  } catch {
    return instance;
  }
};

/** The instance as a link target, or null. Only an http(s) address qualifies:
    the record's instance is text another Hatch sent, and `javascript:` is text
    too. Null for an absent instance and for one that does not parse. */
export function originHref(contribution: ContributionRecord): string | null {
  const instance = contribution.originInstance;
  if (!instance) return null;
  try {
    const url = new URL(instance);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null;
  } catch {
    return null;
  }
}

/** The origin as the record names it, in the two halves the badge draws: the
    key (or `@login` where the record has no origin key) and, where there is
    one, the instance's host. */
export function originParts(contribution: ContributionRecord): { name: string; host: string | null } {
  if (contribution.originKey) {
    return {
      name: contribution.originKey,
      host: contribution.originInstance ? hostOf(contribution.originInstance) : null,
    };
  }
  return { name: `@${contribution.authorLogin}`, host: null };
}

/** The origin as one phrase: `KEY · host` where the record has both, `KEY`
    where it has only the key, else `@login`. */
export function originLabel(contribution: ContributionRecord): string {
  const { name, host } = originParts(contribution);
  return host ? `${name} · ${host}` : name;
}

/** The ancestors that carry a contribution record, root first, starting from
    `startKey` (the issue's parent) and walking `parentKey` up through the
    cards the board holds. An ancestor the board does not carry ends the walk,
    since there is no card to read the next parent from; one with no record is
    passed over but still walked through. A cycle ends it too. */
export function originChain(startKey: string | null | undefined, cards: IssueCard[]): IssueCard[] {
  const byKey = new Map(cards.map((card) => [card.key, card]));
  const seen = new Set<string>();
  const chain: IssueCard[] = [];

  let key = startKey ?? null;
  while (key && !seen.has(key)) {
    seen.add(key);
    const card = byKey.get(key);
    if (!card) break;
    if (card.contribution) chain.push(card);
    key = card.parentKey;
  }

  return chain.reverse();
}
