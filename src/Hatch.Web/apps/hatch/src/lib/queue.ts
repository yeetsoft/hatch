/* Drawing a pass over the queue: the sentence each row says, the marker it
   carries, and the tally a header line reads off. */

import type { IssueCard, QueueEntry } from '../types';
import { conflictedChecks } from './mergeCheck';
import { failedBuilds } from './buildCheck';

/** The sentence a queue row says, in the order `BoardCommands.Draw` picks
    one: the reason it is blocked, why it is clear despite carrying none,
    which dispatch it is, or where it is headed. Mirrors `Draw`'s `?:` chain
    exactly - the order is the rule, not a style choice. */
export function queueWords(entry: QueueEntry): string {
  if (entry.blocked) return entry.blocked;
  if (entry.clearNote) {
    return `clear for ${entry.fromStatus.name} -> ${entry.toStatus?.name ?? '?'} - ${entry.clearNote}`;
  }
  if (entry.kind === 'conflicts') {
    const trunk = conflictedChecks(entry.issue.mergeChecks)[0]?.trunk ?? 'the trunk';
    return `resolving conflicts with ${trunk}`;
  }
  if (entry.kind === 'build') {
    // failedBuilds also admits a pending check that already carries a failed
    // check, which the CLI's Builds.Of does not - the same widening belongs
    // here too, for the same reason: a check that has failed counts while
    // the rest of the build is still running.
    const names = [...new Set(failedBuilds(entry.issue.buildChecks).flatMap((c) => c.failing.map((f) => f.name)))];
    return names.length === 0 ? 'fixing its failing build' : `fixing its failing build (${names.join(', ')})`;
  }
  if (entry.hop) {
    const reason = entry.hopKind === 'parent' ? 'parent pulled, no session' : 'express, no session';
    return `-> ${entry.toStatus?.name ?? '?'}  (${reason})`;
  }
  return `-> ${entry.toStatus?.name ?? '?'}`;
}

/** `!!` for emergency, `!` for expedited, null for normal - the marker a row
    carries. Unlike the CLI's `"!!"`/`"! "`, not padded to a fixed width: that
    padding exists only to keep a monospace column aligned, which a modal row
    does with CSS instead. */
export function queueMarker(entry: QueueEntry): '!!' | '!' | null {
  if (entry.issue.priority === 'emergency') return '!!';
  if (entry.issue.priority === 'expedited') return '!';
  return null;
}

/** Whether any row in the pass carries a marker at all - `Draw`'s
    `anyFirst`, which is what decides whether the column is drawn in the
    first place. */
export function anyAboveNormal(queue: readonly QueueEntry[]): boolean {
  return queue.some((q) => q.issue.priority !== 'normal');
}

/** Whether any row's own issue is claimed right now - the same idiom as
    `anyAboveNormal`, read once for the pass so the robot column is either
    drawn on every row or not drawn at all. A column drawn only on the
    claimed rows would shift the key, badge and pill of those rows out of
    line with the rest.

    A relative's claim does not count: this reads `issue.claim`, never the
    row's `blocked` sentence, so a row folded because an ancestor or a
    descendant is claimed does not by itself draw the column. */
export function anyClaimed(queue: readonly QueueEntry[]): boolean {
  return queue.some((q) => q.issue.claim !== null);
}

/** How many rows the pass looked at, and how many of them are clear (carry
    no `blocked`) - the tally a modal's header line reads off, computed once
    rather than recounted by every consumer. */
export function queueTally(queue: readonly QueueEntry[]): { total: number; clear: number } {
  return { total: queue.length, clear: queue.filter((q) => q.blocked === null).length };
}

/** The board's own card for a queue row, found by key rather than cast - a
    `QueueEntry.issue` is a full `Issue`, not the `IssueCard` the board's
    `onTake` wants, and the two reads (the queue's, the board's) are a moment
    apart, so a row's issue may not be among `cards` at all. */
export function queueCard(entry: QueueEntry, cards: readonly IssueCard[]): IssueCard | undefined {
  return cards.find((c) => c.key === entry.issue.key);
}
