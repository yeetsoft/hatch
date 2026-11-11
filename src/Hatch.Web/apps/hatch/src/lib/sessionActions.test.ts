import { describe, expect, it, vi } from 'vitest';
import { sessionActions } from './sessionActions';
import type { RunnerSession } from '../types';

const session = (id: number): RunnerSession => ({
  id,
  where: null,
  kind: 'loop',
  host: null,
  state: 'running',
  startedAt: '2026-09-08T11:00:00Z',
  lastSeenAt: '2026-09-08T12:00:00Z',
  endedAt: null,
  claimKey: null,
  agent: null,
  os: null,
  exhaustedUntil: null,
  throttledUntil: null,
});

const deps = () => ({
  client: {
    patchRunnerSession: vi.fn(async () => undefined),
    orderRunnerSession: vi.fn(async () => undefined),
    withdrawSessionOrder: vi.fn(async () => undefined),
    pauseAllSessions: vi.fn(async () => undefined),
    stopAllSessions: vi.fn(async () => undefined),
  },
  reload: vi.fn(async () => undefined),
});

describe('sessionActions', () => {
  it('pauses session 2 alone: one call, with its id, never session 1', async () => {
    const { client, reload } = deps();

    await expect(sessionActions('Quill', client, reload).pause(session(2))).resolves.toBeNull();

    expect(client.patchRunnerSession).toHaveBeenCalledTimes(1);
    expect(client.patchRunnerSession).toHaveBeenCalledWith('Quill', 2, { state: 'paused' });
    expect(client.patchRunnerSession).not.toHaveBeenCalledWith('Quill', 1, expect.anything());
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('resumes with running and stops with stopping', async () => {
    const { client, reload } = deps();
    const actions = sessionActions('Quill', client, reload);

    await actions.resume(session(3));
    await actions.stop(session(3));

    expect(client.patchRunnerSession).toHaveBeenNthCalledWith(1, 'Quill', 3, { state: 'running' });
    expect(client.patchRunnerSession).toHaveBeenNthCalledWith(2, 'Quill', 3, { state: 'stopping' });
  });

  it('orders and withdraws on the session it was given', async () => {
    const { client, reload } = deps();
    const actions = sessionActions('Quill', client, reload);

    await actions.order(session(2), { kind: 'work', key: 'HA-12' });
    await actions.order(session(2), { kind: 'pass' });
    await actions.withdraw(session(2));

    expect(client.orderRunnerSession).toHaveBeenNthCalledWith(1, 'Quill', 2, { kind: 'work', key: 'HA-12' });
    expect(client.orderRunnerSession).toHaveBeenNthCalledWith(2, 'Quill', 2, { kind: 'pass' });
    expect(client.withdrawSessionOrder).toHaveBeenCalledTimes(1);
    expect(client.withdrawSessionOrder).toHaveBeenCalledWith('Quill', 2);
  });

  it('pauses all and stops all with one call each, and touches no single session', async () => {
    const { client, reload } = deps();
    const actions = sessionActions('Quill', client, reload);

    await actions.pauseAll();
    await actions.stopAll();

    expect(client.pauseAllSessions).toHaveBeenCalledTimes(1);
    expect(client.pauseAllSessions).toHaveBeenCalledWith('Quill');
    expect(client.stopAllSessions).toHaveBeenCalledTimes(1);
    expect(client.stopAllSessions).toHaveBeenCalledWith('Quill');
    expect(client.patchRunnerSession).not.toHaveBeenCalled();
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it('returns the refusal and still reloads once when the call throws', async () => {
    const { client, reload } = deps();
    client.stopAllSessions.mockRejectedValue(new Error('not yours to stop'));

    await expect(sessionActions('Quill', client, reload).stopAll()).resolves.toBe('not yours to stop');

    expect(reload).toHaveBeenCalledTimes(1);
  });
});
