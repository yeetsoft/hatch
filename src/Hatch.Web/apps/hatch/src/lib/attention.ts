/* What the bar's attention control says, and when it says it loudly.

   Everything decidable lives here rather than in the components, because this
   app has no component tests: the count, the tone, which of the two review
   emptinesses applies, the accessible sentence, and how long a question has
   been waiting are all pure functions of one answer from the server, and every
   one of them is pinned in attention.test.ts. */

import type { Attention, TrunkBuild } from '../types';

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
 */
export function attentionCount(attention: Attention | null): number {
  if (attention === null) return 0;
  return attention.reviews.length + attention.questions.length + (attention.trunkBuilds?.length ?? 0);
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

  const parts: string[] = [];
  if (trunkBuilds > 0) parts.push(`${count(trunkBuilds, 'trunk build')} failing`);
  if (attention.reviews.length > 0) parts.push(`${count(attention.reviews.length, 'pull request')} to review`);
  if (attention.questions.length > 0) parts.push(`${count(attention.questions.length, 'question')} to answer`);

  const exhausted = attention.exhaustedRunners?.length ?? 0;
  if (exhausted > 0) parts.push(`${count(exhausted, 'runner')} ${exhausted === 1 ? 'is' : 'are'} out of Claude usage`);

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

/** The other section's empty state. One wording: a question either exists or it
    does not, and there is no second kind of nothing to tell apart. */
export const questionEmptyWords = (): string => 'Nothing is waiting on an answer.';

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

/** `1 question`, `2 questions`. Every noun here takes a plain -s. */
const count = (n: number, noun: string): string => `${n} ${noun}${n === 1 ? '' : 's'}`;

/** The conflicts section's empty state - one wording, like the questions'. */
export const conflictEmptyWords = (): string => 'No branch in review has stopped merging.';

/** The failing builds section's empty state - one wording, like the conflicts'. */
export const failingBuildEmptyWords = (): string => 'No build in review is failing.';

/** The trunk builds section's empty state - the Human half's own, since a trunk is nobody's issue. */
export const trunkBuildEmptyWords = (): string => 'No trunk build is failing.';

/**
 * `main in forge.example/owner/repo` - the trunk as the runner reported it,
 * and the repository. Nothing here knows what a trunk is called: a label that
 * only ever read `main` would be a fact about exactly one installation.
 */
export const trunkBuildHead = (build: TrunkBuild): string => `${build.trunk} in ${build.canonical}`;

/**
 * `resets 7:40pm` today, `resets Tuesday at 7:40pm` within the week, `resets
 * Oct 5 at 7:40pm` otherwise - the same escalation a person reads a date with,
 * naming only as much of it as is not already obvious from today.
 */
export function resetWords(exhaustedUntil: string, now: Date): string {
  const at = new Date(exhaustedUntil);
  if (Number.isNaN(at.getTime())) return 'resets at an unknown time';

  const clock = at.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' }).toLowerCase().replace(' ', '');

  if (sameDay(at, now)) return `resets ${clock}`;

  const days = Math.floor((startOfDay(at).getTime() - startOfDay(now).getTime()) / 86_400_000);
  if (days > 0 && days < 7) return `resets ${at.toLocaleDateString(undefined, { weekday: 'long' })} at ${clock}`;

  return `resets ${at.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} at ${clock}`;
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
