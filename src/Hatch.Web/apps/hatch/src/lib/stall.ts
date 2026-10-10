/* The one judgement a client makes about a stall mark, apart from the
   component that draws it - the way claim.ts is apart from ClaimPanel.

   Pure functions, no DOM: this app has no component tests, so every decidable
   part lands here, with a test. */

import type { IssueEvent } from '../types';

/** The three states StallPanel draws: nothing, a stall that will resume on its
    own, or a hold that will not - until Resume now is pressed. */
export type MarkState = 'none' | 'stalled' | 'held';

/**
 * `held` first - it can be true with or without a `stalledAt`, when a person
 * has held an issue nothing has stalled yet. Then `stalledAt`. Otherwise
 * `none`, which is the overwhelming majority of issues.
 */
export function markState(issue: { stalledAt: string | null; held: boolean }): MarkState {
  if (issue.held) return 'held';
  if (issue.stalledAt !== null) return 'stalled';
  return 'none';
}

/**
 * Who last held this issue, or null.
 *
 * There is no column for it - `EfHatchIssue.Held` is a bare bool - so this
 * reads it off the event it was set by instead. `events` arrives newest
 * first (`GET .../events`), so the first `held`-kind event found is the most
 * recent press.
 */
export function heldBy(events: IssueEvent[]): string | null {
  return events.find((e) => e.kind === 'held')?.actor ?? null;
}
