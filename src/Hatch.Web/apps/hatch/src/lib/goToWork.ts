/* What the Go to Work / Pause button does when it is pressed, apart from the
   button that draws it - the way lib/runners.ts is apart from the runner row.

   The plan is a verdict on the runners the page already holds, taken once: the
   button's label and the PATCH bodies come from the same plan, so what the
   button says is what a press sends. Pure functions taking `now`, so a test
   judges every row against one instant. */

import { patchRunner } from '../api/client';
import { message } from './errors';
import { ACTIVITY_TONES, activityWords, runnerActivity } from './runners';
import type { Runner, RunnerPatchRequest } from '../types';

export type GoToWorkPlan =
  | { mode: 'loading' }
  | { mode: 'none' }
  | { mode: 'stopping' }
  | { mode: 'pause'; targets: Runner[] }
  | { mode: 'go'; targets: Runner[] };

/**
 * What a press does to the caller's own runners.
 *
 * Only a loop that is yours and still answering is a target: another person's
 * rows are not the caller's to pause, a `once` run has no pause to give, and a
 * gone row cannot hear the request. The mode comes from `state` rather than
 * from `runnerActivity`, because a running runner that is out of usage reads
 * `exhausted` there and must still be paused, not woken.
 */
export function planGoToWork(runners: Runner[] | null, now: Date): GoToWorkPlan {
  if (runners === null) return { mode: 'loading' };

  const alive = runners.filter((r) => r.yours && r.kind === 'loop' && runnerActivity(r, now) !== 'gone');

  const running = alive.filter((r) => r.state === 'running');
  if (running.length > 0) return { mode: 'pause', targets: running };

  const paused = alive.filter((r) => r.state === 'paused');
  if (paused.length > 0) return { mode: 'go', targets: paused };

  return alive.length > 0 ? { mode: 'stopping' } : { mode: 'none' };
}

/** The line beside the button: `before` + `key` (drawn as a link to the issue)
    + `after`, so a line with no key has all its words in `before`. `more` is
    how many other loops of the caller's the line does not name. */
export interface StatusLine {
  before: string;
  key: string | null;
  after: string;
  tone: 'muted' | 'success' | 'danger' | 'primary';
  more: number;
}

const EMPTY_LINE: StatusLine = { before: '', key: null, after: '', tone: 'muted', more: 0 };

/** One row's line and its rank, 1 the most worth saying. */
function lineFor(r: Runner, now: Date): { rank: number; line: Omit<StatusLine, 'more'> } {
  const keyless = (rank: number, before: string, tone: StatusLine['tone']) => ({
    rank,
    line: { before, key: null, after: '', tone },
  });

  // Silence beats state, so a gone row never reads as working or on its way
  // to stopping, whatever it holds.
  if (runnerActivity(r, now) === 'gone') {
    return r.state === 'running' ? keyless(8, 'Stopped unexpectedly', 'danger') : keyless(9, 'Off', 'muted');
  }

  if (r.claimKey) {
    const key = r.claimKey;
    if (r.state === 'running') {
      const after = r.claimStatus ? ` · ${r.claimStatus}` : '';
      return { rank: 1, line: { before: 'Working ', key, after, tone: ACTIVITY_TONES.working } };
    }
    if (r.state === 'paused') return { rank: 2, line: { before: 'Pausing after ', key, after: '', tone: 'primary' } };
    return { rank: 3, line: { before: 'Stopping after ', key, after: '', tone: 'primary' } };
  }

  if (runnerActivity(r, now) === 'exhausted') return keyless(4, activityWords(r, now), ACTIVITY_TONES.exhausted);
  if (r.state === 'running') {
    const where = r.projects.length > 0 ? r.projects.join(', ') : 'any project';
    return keyless(5, `Idle - nothing to do in ${where}`, ACTIVITY_TONES.idle);
  }
  return r.state === 'paused' ? keyless(6, 'Paused', 'primary') : keyless(7, 'Stopping', 'primary');
}

/**
 * What the caller's own loops are doing, in one line.
 *
 * Each of the caller's loops is ranked by what it is doing and the best rank
 * names the line; a tie goes to the first row, which is the server's
 * most-recently-heard-first order. `null` runners, not loaded yet, is the empty
 * line, which the component draws as nothing.
 */
export function statusLine(runners: Runner[] | null, now: Date): StatusLine {
  if (runners === null) return EMPTY_LINE;

  const mine = runners.filter((r) => r.yours && r.kind === 'loop');
  if (mine.length === 0) return { ...EMPTY_LINE, before: 'No runner' };

  const ranked = mine.map((r) => ({ r, ...lineFor(r, now) }));
  const best = ranked.reduce((a, b) => (b.rank < a.rank ? b : a));
  const others = ranked.filter((x) => x !== best);
  const more = best.rank >= 8 ? others.length : others.filter((x) => x.rank < 8).length;

  return { ...best.line, more };
}

/** The PATCH a press sends to each target. `project` is the filter's key, `''`
    for all of them, which clears the runner's projects. */
export function goToWorkBody(mode: 'pause' | 'go', project: string): RunnerPatchRequest {
  return mode === 'pause' ? { state: 'paused' } : { state: 'running', projects: project ? [project] : [] };
}

/**
 * Sends the plan's PATCH to every target, trying all of them even when one is
 * refused, then rejects with the first refusal's own words. A plan with no
 * targets does nothing.
 */
export async function applyGoToWork(
  plan: GoToWorkPlan,
  project: string,
  patch: typeof patchRunner = patchRunner,
): Promise<void> {
  if (plan.mode !== 'pause' && plan.mode !== 'go') return;

  const results = await Promise.allSettled(plan.targets.map((t) => patch(t.name, goToWorkBody(plan.mode, project))));

  const failed = results.find((r): r is PromiseRejectedResult => r.status === 'rejected');
  if (failed) throw new Error(message(failed.reason));
}
