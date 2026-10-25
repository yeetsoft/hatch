/* What the Go to Work / Pause button does when it is pressed, apart from the
   button that draws it - the way lib/runners.ts is apart from the runner row.

   The plan is a verdict on the runners the page already holds, taken once: the
   button's label and the PATCH bodies come from the same plan, so what the
   button says is what a press sends. Pure functions taking `now`, so a test
   judges every row against one instant. */

import { patchRunner } from '../api/client';
import { message } from './errors';
import { runnerActivity } from './runners';
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
