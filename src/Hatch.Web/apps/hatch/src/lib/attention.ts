/* What the bar's attention control says, and when it says it loudly.

   Everything decidable lives here rather than in the components, because this
   app has no component tests: the count, the tone, which of the two review
   emptinesses applies, the accessible sentence, and how long a question has
   been waiting are all pure functions of one answer from the server, and every
   one of them is pinned in attention.test.ts. */

import type { Attention, RunnerCapacity, StalledIssue, TrainStalled, TrunkBuild, WaitingOnARunner } from '../types';

/** Resting or loud. Named for what it is doing rather than for how it looks, so
    a later change to the loud state's colour is not a rename. `isWaiting` is
    already taken by schedule.ts, for a ready date that has not arrived. */
export type AttentionTone = 'rest' | 'asking';

/**
 * How many rows the panel would draw.
 *
 * Reviews plus questions plus trunk builds, and deliberately not
 * `inReviewWithoutPullRequest`. An issue standing in review with nowhere to
 * review it is something a person cannot act on from here, and a control that
 * stayed lit for one would be a control nobody reads after a week. It is said
 * out loud in the section's empty state instead - see `reviewEmptyWords`.
 *
 * Nor `conflicts`. A branch that has stopped merging is the loop's to fix, and
 * one it cannot fix becomes a stall, which is a question, which is counted
 * here already. Listed beside the rest, and never lit for.
 *
 * Nor `failingBuilds`, for the same reason: the loop fixes a failing build, and
 * one it cannot fix becomes a question.
 *
 * `trunkBuilds` is different, and does count: nothing dispatches at a trunk, so
 * a failing one has no agent already on it the way a failing branch build
 * does - it sits until a person presses *File a bug*, which is exactly what
 * the badge exists to surface.
 *
 * `approvedHeld` counts too, unlike `conflicts` and `failingBuilds`: nothing in
 * the merge column is an agent's to fix (HA-289), so every row there is a
 * person's, every time - there is no loop already on it the way there is for
 * a review-column conflict or a failing build.
 *
 * `landingHeld` counts for the same reason: nothing dispatches at an issue
 * whose work already reached the trunk, so every row is a person's to finish.
 *
 * `noCodeHeld` counts for the same reason: a ticket in review that finished
 * with no code has no pull request to merge and no pre-approval to close it,
 * so every row is a person's to close.
 *
 * `contributionsHeld` counts for the same reason: a contribution in review
 * with a conflict or a red build, in a project that has not asked runners to
 * work those, waits for a person.
 *
 * `deadlocked` counts for the same reason: nothing dispatches into a ring, so
 * every row is a person's to break.
 *
 * `backup` counts for the same reason: nothing in the loop will fix a folder,
 * so a backup that is failing or stale is a person's to fix, every time.
 * Healthy and unconfigured both arrive as null, and add nothing.
 *
 * `waitingOnARunner` counts for the same reason as `deadlocked`: no loop is on
 * it, a move no live runner can take sits until a person fixes the runner (a
 * model or a tool), so every row is a person's.
 *
 * `runnerCapacity` counts only its rows with no taker: a one-taker row is
 * information, not breakage (HA-2079's decision), while a no-taker row is a
 * person's to fix, the same as `waitingOnARunner`.
 *
 * `approvedWaiting` is deliberately left out: the loop is still progressing
 * those on its own, and a row it cannot clear becomes `approvedHeld`, already
 * counted above.
 *
 * `stalled` counts only its board-held rows (`kind: 'person'`, `held: false`):
 * nothing dispatches into a board-held row, so every one is a person's to
 * break, the same as `deadlocked`. A `loop`-kind row and a person-held row are
 * both still the loop's or a person's own choice to sit on, and neither counts.
 */
export function attentionCount(attention: Attention | null): number {
  if (attention === null) return 0;
  return (
    attention.reviews.length +
    attention.questions.length +
    (attention.trunkBuilds?.length ?? 0) +
    (attention.approvedHeld?.length ?? 0) +
    (attention.landingHeld?.length ?? 0) +
    (attention.noCodeHeld?.length ?? 0) +
    (attention.contributionsHeld?.length ?? 0) +
    (attention.deadlocked?.length ?? 0) +
    (attention.waitingOnARunner?.length ?? 0) +
    (attention.runnerCapacity?.filter((r) => r.takers.length === 0).length ?? 0) +
    (attention.backup ? 1 : 0) +
    (attention.stalled?.filter((s) => s.kind === 'person' && !s.held).length ?? 0)
  );
}

/**
 * What the bar's attention pills add up to at phone width, where they fold
 * into one count: exactly the four sections that draw a pill - deadlocked,
 * trunk, review and question. Deliberately not `attentionCount`, which also
 * counts `approvedHeld`, `landingHeld`, `noCodeHeld`, `waitingOnARunner`, `runnerCapacity` and `backup`: those light the control and have
 * a row in the panel, but no pill, so a total that included them would not
 * be the sum of the pills it stands in for.
 */
export function pillCount(attention: Attention | null): number {
  if (attention === null) return 0;
  return (
    (attention.deadlocked?.length ?? 0) +
    (attention.trunkBuilds?.length ?? 0) +
    attention.reviews.length +
    attention.questions.length
  );
}

/** Loud only when there is something a person can act on. Nothing read yet is
    resting, not a third state: "we have not asked yet" and "nothing is waiting"
    are the same thing to draw. */
export const attentionTone = (attention: Attention | null): AttentionTone =>
  attentionCount(attention) > 0 ? 'asking' : 'rest';

/**
 * What the control is called, in words - which is the whole of what a screen
 * reader gets, and half of what makes the loud state legible without colour.
 *
 * `1 trunk build failing, 2 pull requests to review, 1 question to answer`,
 * any part dropped when it is empty, and one sentence at rest. Pluralised on
 * every part, because `1 pull requests` read out loud is the kind of thing
 * that makes somebody stop trusting the rest of the sentence.
 */
export function attentionLabel(attention: Attention | null): string {
  if (attention === null) return 'Nothing is waiting on you';

  const trunkBuilds = attention.trunkBuilds?.length ?? 0;
  const deadlocked = attention.deadlocked?.length ?? 0;
  const stalledCount = attention.stalled?.filter((s) => s.kind === 'person' && !s.held).length ?? 0;

  const parts: string[] = [];
  if (trunkBuilds > 0) parts.push(`${count(trunkBuilds, 'trunk build')} failing`);
  if (attention.backup) parts.push('backup needs attention');
  if (deadlocked > 0) parts.push(`${count(deadlocked, 'issue')} deadlocked`);
  if (attention.questions.length > 0) parts.push(`${count(attention.questions.length, 'question')} to answer`);
  if (stalledCount > 0) parts.push(`${count(stalledCount, 'issue')} stalled`);
  if (attention.reviews.length > 0) parts.push(`${count(attention.reviews.length, 'pull request')} to review`);

  const exhausted = attention.exhaustedRunners?.length ?? 0;
  if (exhausted > 0) parts.push(`${count(exhausted, 'runner')} ${exhausted === 1 ? 'is' : 'are'} out of usage`);

  return parts.length > 0 ? parts.join(', ') : 'Nothing is waiting on you';
}

/**
 * Which emptiness the pull request section is in.
 *
 * Three are different facts and the difference is the point. Nothing in
 * review at all is a quiet board. Things in review with no pull request
 * recorded is a ticket whose agent forgot `hatch pr` - invisible everywhere
 * else, and the reason this wording exists rather than one flat "nothing here".
 * And a pull request held back by a conflict or a failed build is not this
 * section's to show either - it is already in the Agent group, and this is
 * where a person is told it is not theirs to look at yet. The two wordings
 * combine when both counts are nonzero, no-pull-request first.
 */
export function reviewEmptyWords(attention: Attention | null): string {
  const without = attention?.inReviewWithoutPullRequest ?? 0;
  const heldBack = attention?.reviewsHeldBack ?? 0;

  const parts: string[] = [];
  if (without > 0) parts.push(without === 1
    ? '1 issue is in review with no pull request recorded.'
    : `${without} issues are in review with no pull request recorded.`);
  if (heldBack > 0) parts.push(heldBack === 1
    ? '1 pull request is waiting on the loop.'
    : `${heldBack} pull requests are waiting on the loop.`);

  return parts.length > 0 ? parts.join(' ') : 'Nothing is up for review.';
}

/**
 * Whether `reviewEmptyWords` says anything other than its plain "nothing
 * here" sentence - the same fact as a boolean, for a row count rather than
 * words. The two must agree: this is true exactly when `reviewEmptyWords`
 * would print something other than `'Nothing is up for review.'`.
 */
export function reviewWordsCarryAFact(attention: Attention | null): boolean {
  return (attention?.inReviewWithoutPullRequest ?? 0) > 0 || (attention?.reviewsHeldBack ?? 0) > 0;
}

/** The other section's empty state. One wording: a question either exists or it
    does not, and there is no second kind of nothing to tell apart. */
export const questionEmptyWords = (): string => 'Nothing is waiting on an answer.';

/** The stalled section's empty state - one wording, like the questions'. */
export const stalledEmptyWords = (): string => 'Nothing is stalled.';

/**
 * How long a question has been waiting: `just asked`, `4 minutes`, `2 hours`,
 * `3 days`.
 *
 * Its own rather than `agePhrase` from utilization.ts, which says `read 3 hours
 * ago` - that is the battery's sentence about a reading it took, and this is a
 * duration sitting beside a question in a list. It also has to reach days: a
 * reading is never more than a few hours old and a question can sit over a
 * weekend, which is exactly when somebody most needs to see how long.
 */
export function waitedWords(askedAt: string, now: Date): string {
  const at = Date.parse(askedAt);
  if (Number.isNaN(at)) return 'waiting';

  const minutes = Math.floor(Math.max(0, now.getTime() - at) / 60_000);
  if (minutes < 1) return 'just asked';
  if (minutes < 60) return count(minutes, 'minute');

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return count(hours, 'hour');

  return count(Math.floor(hours / 24), 'day');
}

/**
 * When a stall resumes on its own: `in 12 minutes`, `in 1 minute`, `any moment
 * now` once the window has passed, or `will not resume on its own` when the
 * install has turned unattended resuming off (`stallResumeSeconds <= 0`,
 * mirroring `Dispatch.Blocked`'s own branch for the same setting).
 *
 * A bare duration fragment, in `waitedWords`'s register, composed into a
 * sentence by the component rather than here - this only answers "when",
 * never "why" or "that it is held instead".
 */
export function resumeWords(stalledAt: string, stallResumeSeconds: number, now: Date): string {
  if (stallResumeSeconds <= 0) return 'will not resume on its own';

  const at = Date.parse(stalledAt);
  if (Number.isNaN(at)) return 'resumes on its own';

  const remainingMs = at + stallResumeSeconds * 1000 - now.getTime();
  if (remainingMs <= 0) return 'any moment now';

  const minutes = Math.ceil(remainingMs / 60_000);
  return `in ${count(minutes, 'minute')}`;
}

/**
 * One stalled row's own sentence: `resumes in 12 minutes` or `any moment now`
 * for a loop-kind row, `held by a person` for one a person has held, or the
 * queue's own sentence for a board-held row.
 *
 * The `'any moment now'` exception is deliberate: that string already reads
 * as a complete sentence on its own, where `'in 12 minutes'` does not without
 * the `resumes` prefix.
 */
export function stalledWords(row: StalledIssue, stallResumeSeconds: number, now: Date): string {
  if (row.kind === 'loop') {
    const w = resumeWords(row.stalledAt!, stallResumeSeconds, now);
    return w === 'any moment now' ? w : `resumes ${w}`;
  }
  if (row.held) return 'held by a person';
  return row.sentence ?? '';
}

/** `1 question`, `2 questions`. Every noun here takes a plain -s. */
const count = (n: number, noun: string): string => `${n} ${noun}${n === 1 ? '' : 's'}`;

/** The conflicts section's empty state - one wording, like the questions'. */
export const conflictEmptyWords = (): string => 'No branch in review has stopped merging.';

/** The failing builds section's empty state - one wording, like the conflicts'. */
export const failingBuildEmptyWords = (): string => 'No build in review is failing.';

/** The approved-but-held section's empty state - one wording, like the conflicts' and failing builds'. */
export const approvedHeldEmptyWords = (): string => 'Nothing approved has stopped merging.';

/** The empty state of the "Landed, not done" section. */
export const landingHeldEmptyWords = (): string => 'Nothing landed has stopped short of done.';

/** The empty state of the "Finished with no code" section. */
export const noCodeHeldEmptyWords = (): string => 'Nothing finished with no code is waiting on you.';

/** The empty state of the "Contributions runners will not work" section. */
export const contributionsHeldEmptyWords = (): string => 'No contribution in review is waiting on you.';

/** The empty state of the "Deadlocked" section. */
export const deadlockedEmptyWords = (): string => 'Nothing is deadlocked.';

/** The empty state of the "Waiting on a runner" section. */
export const waitingOnARunnerEmptyWords = (): string => 'Nothing is waiting on a runner.';

const NO_LIVE_RUNNER = 'no live runner ';

/**
 * The Runners page's banner: one line per distinct `missing` sentence, commonest
 * first and then alphabetically - `hatch queue`'s own grouping and wording, so
 * `2 issues wait for a runner that has postgres 18 or newer` reads the same in
 * both places. A sentence that is not the plain `no live runner <x>` shape is
 * said whole, after a colon.
 */
export function waitingOnARunnerLines(waiting: WaitingOnARunner[]): string[] {
  const groups = new Map<string, number>();
  for (const w of waiting) groups.set(w.missing, (groups.get(w.missing) ?? 0) + 1);

  return [...groups]
    .sort(([am, an], [bm, bn]) => bn - an || (am < bm ? -1 : am > bm ? 1 : 0))
    .map(([missing, n]) => {
      const verb = n === 1 ? 'waits' : 'wait';
      return missing.startsWith(NO_LIVE_RUNNER) && !missing.includes('; ')
        ? `${count(n, 'issue')} ${verb} for a runner that ${missing.slice(NO_LIVE_RUNNER.length)}`
        : `${count(n, 'issue')} ${verb}: ${missing}`;
    });
}

/**
 * A runner capacity row's second line: `3 clear steps: only Nathan-PC can take
 * them` when one runner can, or `no live runner can take them: Nathan-PC:
 * out of usage; Nathan-MBP: wrong model` when none can. The project key is
 * left out - the row head already carries it, and no sibling section's
 * second line repeats its own key either. `takers.length` is never 2+ here:
 * the server only lists a project when it has at most one taker.
 */
export function runnerCapacityWords(row: RunnerCapacity): string {
  if (row.takers.length === 1) return `${count(row.steps, 'clear step')}: only ${row.takers[0]} can take them`;
  return `no live runner can take them: ${row.reasons.map((r) => `${r.runner}: ${r.reason}`).join('; ')}`;
}

/** The empty state of the "Runner capacity" section. */
export const runnerCapacityEmptyWords = (): string => "Every project's next steps have two or more live runners.";

/** What a "Stalled merge trains" row says: who was passed over, and who has the train now. */
export function trainStalledWords(t: TrainStalled): string {
  const passedOver = `${t.passedOverRunner} was passed over on ${t.projectName}'s train`;
  return t.electedRunner === null ? `${passedOver} - no other runner is eligible` : `${passedOver} - handed to ${t.electedRunner}`;
}

/** The trunk builds section's empty state - the Human half's own, since a trunk is nobody's issue. */
export const trunkBuildEmptyWords = (): string => 'No trunk build is failing.';

/**
 * `main in forge.example/owner/repo` - the trunk as the runner reported it,
 * and the repository. Nothing here knows what a trunk is called: a label that
 * only ever read `main` would be a fact about exactly one installation.
 */
export const trunkBuildHead = (build: TrunkBuild): string => `${build.trunk} in ${build.canonical}`;

/**
 * `7:40pm` today, `Tuesday at 7:40pm` within the week, `Oct 5 at 7:40pm`
 * otherwise - the same escalation a person reads a date with, naming only as
 * much of it as is not already obvious from today. `null` when `at` cannot be
 * told at all.
 */
export function resetClockWords(at: Date, now: Date): string | null {
  if (Number.isNaN(at.getTime())) return null;

  const clock = at.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' }).toLowerCase().replace(' ', '');

  if (sameDay(at, now)) return clock;

  const days = Math.floor((startOfDay(at).getTime() - startOfDay(now).getTime()) / 86_400_000);
  if (days > 0 && days < 7) return `${at.toLocaleDateString(undefined, { weekday: 'long' })} at ${clock}`;

  return `${at.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} at ${clock}`;
}

/**
 * `resets 7:40pm` today, `resets Tuesday at 7:40pm` within the week, `resets
 * Oct 5 at 7:40pm` otherwise - the escalation `resetClockWords` computes, worded
 * as the reason a runner is unusable rather than the clock alone.
 */
export function resetWords(exhaustedUntil: string, now: Date): string {
  const words = resetClockWords(new Date(exhaustedUntil), now);
  return words === null ? 'resets at an unknown time' : `resets ${words}`;
}

const startOfDay = (d: Date): Date => new Date(d.getFullYear(), d.getMonth(), d.getDate());
const sameDay = (a: Date, b: Date): boolean => startOfDay(a).getTime() === startOfDay(b).getTime();

// ---- The two icons on a row under Pull requests to review ----

/**
 * `Build passed`, `Build failed`, `No build results yet` - the build icon's
 * accessible name, in words rather than colour alone.
 *
 * `failure` is drawn and pinned here even though a real row can never carry
 * it: a pull request with any failing check is held back into *Builds that
 * fail* before it ever reaches this list, so this state is reachable only by
 * calling the function directly, as the ticket asked for a tri-state icon.
 */
export function buildIconWords(buildState: string): string {
  if (buildState === 'success') return 'Build passed';
  if (buildState === 'failure') return 'Build failed';
  return 'No build results yet';
}

/** Which token colours the build icon: `--success`, `--danger` or `--muted`. */
export function buildIconTone(buildState: string): 'success' | 'danger' | 'muted' {
  if (buildState === 'success') return 'success';
  if (buildState === 'failure') return 'danger';
  return 'muted';
}

/**
 * `Up to date with main`, `Behind main`, `Not checked against the trunk yet` -
 * naming the trunk as the runner reported it, never a hardcoded name.
 */
export function trunkIconWords(holdsTrunk: boolean | null, trunk: string | null): string {
  if (holdsTrunk === null) return 'Not checked against the trunk yet';
  return holdsTrunk ? `Up to date with ${trunk}` : `Behind ${trunk}`;
}

/**
 * `--success` or `--muted` for the up-to-date icon. Behind is not an alarm -
 * criterion 10 is explicit that a branch merely behind the trunk is not held
 * back - so it shares `--muted` with "not checked" rather than a fourth
 * token; the words, not the colour, are what tell the two apart.
 */
export function trunkIconTone(holdsTrunk: boolean | null): 'success' | 'muted' {
  return holdsTrunk === true ? 'success' : 'muted';
}

/**
 * Whether a press inside the open panel should close it.
 *
 * Yes for a plain press on a link that stays in this tab: the row has done its
 * job, and a panel left over the page it just loaded - the whole screen, on a
 * phone - is the bug this exists for. No for everything else: no link (a
 * button such as *File a bug*, or the text between rows), a link that opens a
 * new tab (the person comes back to the panel where they left it), and a press
 * with a modifier or a button other than the primary, which opens elsewhere
 * and leaves this page alone.
 *
 * `link` is the nearest anchor to the press, or null when there is none.
 */
export function closesOnPress(
  link: { href: string | null; target: string | null } | null,
  press: { button: number; metaKey: boolean; ctrlKey: boolean; shiftKey: boolean; altKey: boolean },
): boolean {
  if (link === null || !link.href) return false;
  if (link.target === '_blank') return false;
  if (press.button !== 0) return false;
  return !(press.metaKey || press.ctrlKey || press.shiftKey || press.altKey);
}

/**
 * Whether a section collapses to its empty-state sentence alone, with no row
 * beneath it. True only when there are no rows to draw and the empty state
 * itself carries no fact worth keeping open for - a section whose empty
 * state still says something, such as `reviewEmptyWords`'s "waiting on the
 * loop", stays open to say it.
 */
export function sectionCollapsed(rowCount: number, emptyWordsCarryAFact = false): boolean {
  return rowCount === 0 && !emptyWordsCarryAFact;
}

/**
 * Whether a whole group - Human or Agent - collapses: every section inside
 * it has nothing to draw. True for an empty list too, since a group with no
 * sections has nothing open either.
 */
export function groupCollapsed(sectionRowCounts: number[]): boolean {
  return sectionRowCounts.every((rowCount) => rowCount === 0);
}

/**
 * One row count per Human section, in the panel's own order, for
 * `groupCollapsed` to fold over: trunk builds, backup, stalled merge trains,
 * deadlocked, questions, stalled, reviews, approved-held, landing-held,
 * no-code-held, contributions-held, runner capacity, waiting on a runner.
 *
 * `backup` is a single fact rather than a list - one row when it is not
 * null, none when it is. `reviews` counts at least one row whenever
 * `reviewWordsCarryAFact` is true, even with an empty `reviews` list, so a
 * nonempty empty-state sentence keeps that section open the same way a row
 * would.
 */
export function humanSectionRowCounts(attention: Attention | null): number[] {
  if (attention === null) return [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
  return [
    (attention.trunkBuilds ?? []).length,
    attention.backup ? 1 : 0,
    (attention.trainStalled ?? []).length,
    (attention.deadlocked ?? []).length,
    (attention.questions ?? []).length,
    (attention.stalled ?? []).length,
    Math.max((attention.reviews ?? []).length, reviewWordsCarryAFact(attention) ? 1 : 0),
    (attention.approvedHeld ?? []).length,
    (attention.landingHeld ?? []).length,
    (attention.noCodeHeld ?? []).length,
    (attention.contributionsHeld ?? []).length,
    (attention.runnerCapacity ?? []).length,
    (attention.waitingOnARunner ?? []).length,
  ];
}

/** One row count per Agent section, in the panel's own order: conflicts, then failing builds. */
export function agentSectionRowCounts(attention: Attention | null): number[] {
  if (attention === null) return [0, 0];
  return [(attention.conflicts ?? []).length, (attention.failingBuilds ?? []).length];
}
