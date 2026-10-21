/* What a runner row says about itself, apart from the page that draws it - the
   way lib/claim.ts is apart from the badge.

   The server hands over four facts and no verdict: when the runner was last
   heard from, how long silence is allowed to last, what it holds, and what it
   was asked to do. Turning those into one word is this file's whole job, and it
   is here rather than in the page because the ordering between them is a
   judgement worth testing: a runner that has stopped answering is *gone*
   whatever it was asked to do, because "we told it to pause" says nothing about
   whether it is still there to have heard.

   Pure functions taking `now` rather than reading it, so a page judges every
   row against one instant. */

import { resetClockWords } from './attention';
import { agoPhrase } from './claim';
import type { Runner, RunnerPatchRequest } from '../types';

/** The six states a row can be in, in the order they are decided. */
export type RunnerActivity = 'gone' | 'exhausted' | 'paused' | 'stopping' | 'working' | 'idle';

/** The Max spend field's hint, naming the one thing the cap cannot do: bind an
    agent whose session reports no price at all. */
export const MAX_SPEND_HINT =
  'Dollars, on the same terms. It counts only sessions whose agent reports a price, so it cannot bind one that does not.';

/**
 * What this runner is doing, in one word.
 *
 * Silence beats everything, which is the one ordering decision here: a
 * runner that stopped heartbeating is `gone` whatever it last reported about
 * itself, because silence is a fact and everything else is a stale claim.
 * Below that, a fact the runner reports about its own account -
 * `exhaustedUntil` - beats a request a person made of it: an operator who
 * paused a runner still sees that it is out of usage, because pausing
 * it changes nothing about whether it has any usage to spend. That fact is
 * read the same way `gone`'s heartbeat is, though: a reset time only counts
 * while it is still ahead of `now` - once it has passed, it is no longer a
 * fact about the account, the same as a heartbeat old enough to be silence.
 * Below that, what it was asked to do beats what it is holding - a loop
 * finishing its last increment is `stopping`, and that is the thing somebody
 * watching wants to see.
 */
export function runnerActivity(runner: Runner, now: Date): RunnerActivity {
  const beat = Date.parse(runner.lastSeenAt);

  // An unparseable heartbeat is the one case where "look at this" is the honest
  // answer: it is a row whose age cannot be told, not a row that is fine.
  if (Number.isNaN(beat) || now.getTime() - beat > runner.goneAfterSeconds * 1000) return 'gone';

  if (runner.exhaustedUntil !== null && Date.parse(runner.exhaustedUntil) > now.getTime()) return 'exhausted';
  if (runner.state === 'paused') return 'paused';
  if (runner.state === 'stopping') return 'stopping';

  return runner.claimKey ? 'working' : 'idle';
}

/** The same, as the words on the row. */
export function activityWords(runner: Runner, now: Date): string {
  const activity = runnerActivity(runner, now);
  switch (activity) {
    case 'gone':
      return 'Gone';
    case 'exhausted': {
      const words = resetClockWords(new Date(runner.exhaustedUntil as string), now);
      return `Out of usage until ${words ?? 'an unknown time'}`;
    }
    case 'paused':
      return 'Paused';
    case 'stopping':
      return 'Stopping';
    case 'working':
      return `Working ${runner.claimKey}`;
    default:
      return 'Idle';
  }
}

/** What each activity is worth saying loudly. Working is the one worth a colour
    - it is the row that is spending money right now - and gone is the one worth
    a warning. */
export const ACTIVITY_TONES: Record<RunnerActivity, 'muted' | 'success' | 'danger' | 'primary'> = {
  working: 'success',
  stopping: 'primary',
  paused: 'primary',
  exhausted: 'danger',
  gone: 'danger',
  idle: 'muted',
};

/** One row of the strip the queue modal draws: every per-runner decision the
    strip needs, already made, so the component only draws. */
export interface RunnerStripRow {
  name: string;
  where: string | null;
  activity: RunnerActivity;
  words: string;
  tone: 'muted' | 'success' | 'danger' | 'primary';
  claimKey: string | null;
  line: string | null;
  ago: string;
  gone: boolean;
}

/**
 * The strip's rows, in the order the runners were given - the server's order,
 * most recently heard from first, is the order, so this does not sort.
 *
 * `claimKey` and `line` belong to a working row only: the strip draws the key
 * beside the badge as a link, so a ghost, an idle runner and a paused one
 * carry neither, and `words` says plainly `Working` rather than
 * `activityWords`' `Working AER-12`, which would say the key twice.
 */
export function runnerStrip(runners: readonly Runner[], now: Date): RunnerStripRow[] {
  return runners.map((runner) => {
    const activity = runnerActivity(runner, now);
    const working = activity === 'working';

    return {
      name: runner.name,
      where: runner.where,
      activity,
      words: working ? 'Working' : activityWords(runner, now),
      tone: ACTIVITY_TONES[activity],
      claimKey: working ? runner.claimKey : null,
      line: working ? runner.line : null,
      ago: agoPhrase(runner.lastSeenAt, now),
      gone: activity === 'gone',
    };
  });
}

/**
 * Whether this row has controls at all.
 *
 * A `once` runner is one increment that has already read whatever it was going
 * to read; a Pause on it would be a button nothing will ever look at, which is
 * worse than no button.
 */
export const isControllable = (runner: Runner) => runner.kind === 'loop';

/** The repositories a runner serves, as one phrase - or null when it has
    named none. */
export function repositoriesLabel(runner: Runner): string | null {
  if (runner.repositories.length === 0) return null;
  const repos = runner.repositories.join(', ');
  return runner.clones ? `${repos}, clones what it lacks` : repos;
}

/** A runner's tags, joined - or null where it has named none. */
export function tagsLabel(runner: Runner): string | null {
  return runner.tags.length === 0 ? null : runner.tags.join(', ');
}

/** Whether this runner was started with do-my-work or --mine, as the word for the row - or null, drawn the same as `clones`. */
export function mineLabel(runner: Runner): string | null {
  return runner.mine ? 'own tickets only' : null;
}

/** The trains this runner drives, as one phrase - or the sentence saying it
    drives none, the one way this label is louder than repositoriesLabel and
    mineLabel: AC5 asks the row to say it is driving none, not say nothing. */
export function drivesLabel(runner: Runner): string {
  return runner.drives.length === 0 ? 'driving nothing' : `driving ${runner.drives.join(', ')}`;
}

/** The bounds as the form holds them: strings, because that is what inputs are
    and what the wire takes. */
export interface RunnerBounds {
  under: string;
  maxRuns: string;
  maxSpend: string;
  /** The wire form, or `''` - what MomentField hands back. */
  untilAt: string;
}

/** What a row's bounds look like in the form that edits them. */
export function boundsOf(runner: Runner): RunnerBounds {
  return {
    under: runner.under ?? '',
    maxRuns: runner.maxRuns === null ? '' : String(runner.maxRuns),
    maxSpend: runner.maxSpend === null ? '' : String(runner.maxSpend),
    untilAt: runner.untilAt ?? '',
  };
}

/** Whether the form is holding anything the row is not. */
export function boundsChanged(draft: RunnerBounds, runner: Runner): boolean {
  const saved = boundsOf(runner);
  return (['under', 'maxRuns', 'maxSpend', 'untilAt'] as const).some((f) => draft[f].trim() !== saved[f].trim());
}

/**
 * Why the form cannot be saved yet, or null.
 *
 * The one rule worth stating on screen rather than discovering as a refusal:
 * `until` is an hour and not a day. The server says the same thing, and this is
 * only what stops somebody typing a date, pressing Save and being told off for
 * a control that offered them the empty time box in the first place.
 */
export function boundsProblem(draft: RunnerBounds): string | null {
  return draft.untilAt.trim() !== '' && !draft.untilAt.includes('T')
    ? 'An until is an hour, not just a day - set a time beside the date.'
    : null;
}

/**
 * The form as a request: every field, trimmed, including the empty ones.
 *
 * Sending all four rather than only what changed is the point of the tri-state:
 * `''` is how a cap comes off, and a field left out would mean "leave it", so a
 * cleared box that was not sent would silently keep the cap it was clearing.
 */
export function boundsRequest(draft: RunnerBounds): RunnerPatchRequest {
  return {
    under: draft.under.trim(),
    maxRuns: draft.maxRuns.trim(),
    maxSpend: draft.maxSpend.trim(),
    untilAt: draft.untilAt.trim(),
  };
}
