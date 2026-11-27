/* What the Serves section does apart from the component - the split
   lib/budget.ts makes, for the same reason: this app has no DOM test setup,
   so what can be wrong lives in functions with a test beside them.

   The section draws every capability of the ambition above an epic with a tick
   box each; what is here is the draft's arithmetic, the walk up to the
   ambition, and the write. */

import { getIssue, putIssueServes } from '../api/client';
import type { Issue, ServedCapability } from '../types';
import { message } from './errors';

/** How many parents the walk to the ambition follows before it gives up. */
const MAX_HOPS = 12;

/** The capability ids an epic serves now. */
export function servedIds(serves: ServedCapability[] | null): number[] {
  return (serves ?? []).map((row) => row.capabilityId);
}

/** The draft with `id` ticked if it was not, unticked if it was. */
export function toggleId(draft: number[], id: number): number[] {
  return draft.includes(id) ? draft.filter((d) => d !== id) : [...draft, id];
}

/** Whether two drafts name the same capabilities, in any order. */
export function sameSet(a: number[], b: number[]): boolean {
  if (a.length !== b.length) return false;
  const held = new Set(b);

  return a.every((id) => held.has(id));
}

/** The key of the first ambition above `issue`, or null when the chain ends
    at a root with none. Walks parents with a read of each rather than the
    board's cards: a folded or finished ancestor may not be on the board, and
    the server's own rule is "at any depth". A cycle or an unreadable parent
    ends the walk with null. */
export async function findAmbitionAbove(
  issue: Pick<Issue, 'key' | 'parentKey'>,
  read: (key: string) => Promise<Pick<Issue, 'key' | 'type' | 'parentKey'>> = getIssue,
): Promise<string | null> {
  const seen = new Set<string>([issue.key]);
  let next = issue.parentKey;

  for (let hop = 0; hop < MAX_HOPS && next; hop++) {
    if (seen.has(next)) return null;
    seen.add(next);

    let parent: Pick<Issue, 'key' | 'type' | 'parentKey'>;
    try {
      parent = await read(next);
    } catch {
      return null;
    }
    if (parent.type === 'ambition') return parent.key;
    next = parent.parentKey;
  }

  return null;
}

/** Writes the set. Answers null when it went through, or the refusal in the
    server's own words - never throws. */
export async function writeServes(key: string, ids: number[]): Promise<string | null> {
  try {
    await putIssueServes(key, ids);
    return null;
  } catch (err) {
    return message(err);
  }
}
