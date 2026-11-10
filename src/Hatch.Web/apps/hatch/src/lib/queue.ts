/* Drawing a pass over the queue: the sentence each row says, the marker it
   carries, and the tally a header line reads off. */

import type { IssueCard, QueueEntry, Runner } from '../types';
import { conflictedChecks } from './mergeCheck';
import { failedBuilds } from './buildCheck';
import { HttpError } from './errors';

/** The sentence a queue row says, in the order `BoardCommands.Draw` picks
    one: the reason it is blocked, why it is clear despite carrying none,
    which dispatch it is, or where it is headed. Mirrors `Draw`'s `?:` chain
    exactly - the order is the rule, not a style choice. A row the server
    judged takers for ends with the `takersWords` clause in a second
    parenthesis, where one runner or none can take it. */
export function queueWords(entry: QueueEntry): string {
  const words = rowWords(entry);
  const takers = takersWords(entry);
  return takers ? `${words}  (${takers})` : words;
}

/** Who can take a clear row's step, where that is worth saying: one live runner
    alone, or none. Empty where two or more can, or where the server did not
    judge. Mirrors `BoardCommands.TakersWords`. */
export function takersWords(entry: QueueEntry): string {
  const takers = entry.takers;
  if (!takers) return '';
  if (takers.length === 1) return `only ${takers[0]} can take this`;
  if (takers.length > 0) return '';
  const untaken = entry.untaken ?? [];
  if (untaken.length === 0) return 'no live runner can take this';
  return `no live runner can take this: ${untaken.map((u) => `${u.runner}: ${u.reason}`).join('; ')}`;
}

function rowWords(entry: QueueEntry): string {
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
    return `-> ${entry.toStatus?.name ?? '?'}  (${hopReason(entry.hopKind, entry.hopUnder)})`;
  }
  return `-> ${entry.toStatus?.name ?? '?'}`;
}

/** The eight bands `FlowBand` can draw - the four house-accent kinds a
    blocked row already had a word for, and the four HA-967 added tokens
    for. */
export type FlowBandKind = 'clear' | 'working' | 'wip' | 'dependency' | 'turn' | 'family' | 'person' | 'other';

/** The band a card wears for its place in the work queue, or null where it
    wears none: the queue does not read this card at all, or the row in hand
    describes a column the card has already left - a drop paints the card in
    its new column before the reload lands (`BoardPage.tsx`'s `setBoard({
    ...board, issues: placed.issues })`), and the row still held is the one
    for the column the card just left, not the one it is now in. A row whose
    `blockedKind` is null because it came from a server older than HA-937
    reads as `other`, the same as an unnamed block, rather than as no band at
    all. */
export function flowBand(row: QueueEntry | undefined, card: IssueCard): { kind: FlowBandKind; words: string } | null {
  if (!row || row.fromStatus.id !== card.statusId) return null;
  if (row.blocked === null) return { kind: 'clear', words: 'Clear' };
  switch (row.blockedKind) {
    case 'working':
      return { kind: 'working', words: 'Working' };
    case 'wip':
      return { kind: 'wip', words: 'WIP full' };
    case 'dependency':
      return { kind: 'dependency', words: 'Dependency' };
    case 'turn':
      return { kind: 'turn', words: 'In line' };
    case 'family':
      return { kind: 'family', words: 'Family' };
    case 'person':
      return { kind: 'person', words: 'Needs you' };
    case 'other':
    case null:
      return { kind: 'other', words: 'Blocked' };
  }
}

/** The sentence the flow-viz callout carries for a card, or null where the
    card wears no band - `flowBand` is the one place that decides a row is
    stale or absent, so a callout can never open over a card with no band.
    Never composes words of its own: it is `queueWords`, the same line the
    work-queue modal prints on that row. */
export function flowSentence(row: QueueEntry | undefined, card: IssueCard): string | null {
  return row && flowBand(row, card) ? queueWords(row) : null;
}

/** The word a hop row prints, naming which of the six reasons carried it -
    mirrors the CLI's `Picker.HopWord` exactly. */
export function hopWord(hopKind: QueueEntry['hopKind'], hopUnder: string | null): string {
  if (hopKind === 'parent') return 'parent pulled';
  if (hopKind === 'epic') return 'epic';
  if (hopKind === 'under') return `under ${hopUnder}`;
  if (hopKind === 'approve') return 'approved in advance';
  if (hopKind === 'close') return 'closed in advance';
  return 'express';
}

/** `hopWord`, plus the reason nothing was spawned - mirrors `Picker.HopReason`. */
export function hopReason(hopKind: QueueEntry['hopKind'], hopUnder: string | null): string {
  return `${hopWord(hopKind, hopUnder)}, no session`;
}

/** `!!` for emergency, `!` for expedited, `-` for low, `~` for economy, null
    for normal - the marker a row carries. Unlike the CLI's
    `"!!"`/`"! "`/`"- "`/`"~ "`, not padded to a fixed width: that padding
    exists only to keep a monospace column aligned, which a modal row does
    with CSS instead. */
export function queueMarker(entry: QueueEntry): '!!' | '!' | '-' | '~' | null {
  if (entry.issue.priority === 'emergency') return '!!';
  if (entry.issue.priority === 'expedited') return '!';
  if (entry.issue.priority === 'low') return '-';
  if (entry.issue.priority === 'economy') return '~';
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

/** Whether `err` is the queue refusing to answer *as the runner that was
    asked for* - gone, aged out, or any other reason that row cannot answer
    for itself (see WorkController.GetQueue's asRunner branch) - as opposed to
    a 500 or a network failure, which says nothing about whether that runner
    is still there and must not be read as "pick someone else". */
export function isRefusedRunner(err: unknown, askedAsRunner: boolean): boolean {
  return askedAsRunner && err instanceof HttpError && err.status === 400;
}

/** The board's own card for a queue row, found by key rather than cast - a
    `QueueEntry.issue` is a full `Issue`, not the `IssueCard` the board's
    `onTake` wants, and the two reads (the queue's, the board's) are a moment
    apart, so a row's issue may not be among `cards` at all. */
export function queueCard(entry: QueueEntry, cards: readonly IssueCard[]): IssueCard | undefined {
  return cards.find((c) => c.key === entry.issue.key);
}

/** One project's merge train: the rows carrying `mergeWork`, in the order the
    queue already carries them (the dispatcher's own priority order, which
    this must not re-sort), the key of the row that is this train's head, and
    who drives it. Mirrors `Dispatch.Train`'s own grouping and head rule,
    since `IssueProjection.ToDtosAsync` never sets `mergeWork.isHead` - see
    the ticket this patterns, HA-508. */
export interface MergeTrain {
  projectKey: string;
  rows: readonly QueueEntry[];
  /** The key of this train's head row, or null if every row is hold/pushback. */
  headKey: string | null;
  driver: string | null;
}

/** Groups every queue row carrying `mergeWork` into its project's train, in
    first-appearance order, and names each train's head and driver. */
export function mergeTrains(queue: readonly QueueEntry[], runners: readonly Runner[]): MergeTrain[] {
  const order: string[] = [];
  const rows = new Map<string, QueueEntry[]>();
  for (const row of queue) {
    if (row.issue.mergeWork == null) continue;
    const key = row.issue.projectKey;
    if (!rows.has(key)) {
      rows.set(key, []);
      order.push(key);
    }
    rows.get(key)!.push(row);
  }

  return order.map((projectKey) => {
    const projectRows = rows.get(projectKey)!;
    const head = projectRows.find((r) => r.issue.mergeWork!.kind !== 'hold' && r.issue.mergeWork!.kind !== 'pushback');
    return {
      projectKey,
      rows: projectRows,
      headKey: head?.issue.key ?? null,
      driver: runners.find((r) => r.drives.includes(projectKey))?.name ?? null,
    };
  });
}

/** The driver clause a train's row reads, matching the tone `runners.ts`'s
    `drivesLabel` already set. */
export function trainDriverWords(driver: string | null): string {
  return driver === null ? 'no runner is driving it' : `driven by ${driver}`;
}
