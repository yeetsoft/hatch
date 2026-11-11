import { message } from './errors';
import type { RunnerOrderRequest, RunnerSession, RunnerSessionPatchRequest } from '../types';

/** The five `api/client.ts` calls a session's controls make. Passed in, as
    `saveRunner` takes `patch`, so what a press does is testable without a click. */
export interface SessionClient {
  patchRunnerSession: (name: string, id: number, request: RunnerSessionPatchRequest) => Promise<unknown>;
  orderRunnerSession: (name: string, id: number, request: RunnerOrderRequest) => Promise<unknown>;
  withdrawSessionOrder: (name: string, id: number) => Promise<unknown>;
  pauseAllSessions: (name: string) => Promise<unknown>;
  stopAllSessions: (name: string) => Promise<unknown>;
}

/**
 * What each press on the runner detail's Live sessions card does: one call,
 * then ask again.
 *
 * Each returns the sentence the card should show - the server's refusal - or
 * null where the press went through. The reload runs either way, as
 * `saveRunner` does: a refused press must not leave the card drawing what was
 * asked for rather than what is true. The per-session ones act on the one
 * session given, never its siblings; only `pauseAll` and `stopAll` fan out,
 * and the server does that.
 */
export function sessionActions(name: string, client: SessionClient, reload: () => Promise<void>) {
  const run = async (call: () => Promise<unknown>): Promise<string | null> => {
    try {
      await call();
      return null;
    } catch (err) {
      return message(err);
    } finally {
      await reload();
    }
  };

  return {
    pause: (session: RunnerSession) => run(() => client.patchRunnerSession(name, session.id, { state: 'paused' })),
    resume: (session: RunnerSession) => run(() => client.patchRunnerSession(name, session.id, { state: 'running' })),
    stop: (session: RunnerSession) => run(() => client.patchRunnerSession(name, session.id, { state: 'stopping' })),
    order: (session: RunnerSession, request: RunnerOrderRequest) =>
      run(() => client.orderRunnerSession(name, session.id, request)),
    withdraw: (session: RunnerSession) => run(() => client.withdrawSessionOrder(name, session.id)),
    pauseAll: () => run(() => client.pauseAllSessions(name)),
    stopAll: () => run(() => client.stopAllSessions(name)),
  };
}

export type SessionActions = ReturnType<typeof sessionActions>;
