/* What a failing-runner row in the attention panel says, apart from the
   panel so it can be pinned without rendering one.

   Its own file rather than a part of attention.ts, because runners.ts imports
   attention.ts for its clock and this needs runners.ts for its sentence - an
   import the other way round would be a cycle. */

import { failureWords } from './runners';
import type { AttentionRunnerFailure } from '../types';

/**
 * What a person does about a failure of this kind, one sentence.
 *
 * Every kind but the last two is something done on the runner's own machine,
 * which is why those say so; the last two are read, and acted on, on the
 * runner's page. A kind this build does not know gets a plain pointer to the
 * page rather than a guess.
 */
export function runnerFailureAdvice(kind: string): string {
  switch (kind) {
    case 'forge':
      return 'Sign gh in on that machine.';
    case 'signing':
      return 'Fix signing on that machine, or allow unsigned commits on the project.';
    case 'agent-signed-out':
      return 'Sign the agent in on that machine.';
    case 'agent-not-starting':
      return "Read what the agent said, on the runner's page.";
    case 'board-paused':
      return "Read why on the runner's page, where the pause can be lifted.";
    default:
      return "Open the runner's page to see what it cannot do.";
  }
}

/**
 * What the runner cannot do, since when, and what the tool said:
 * `Cannot reach its forge since 7:40pm - gh said: token expired`. The same
 * sentence the Runners page draws, in the viewer's own time zone. The
 * checkout is left out: the row's head already names it.
 */
export const runnerFailureRowWords = (f: AttentionRunnerFailure, now: Date): string => failureWords(f, now);
