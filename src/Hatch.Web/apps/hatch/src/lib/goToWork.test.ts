import { describe, expect, it } from 'vitest';
import { applyGoToWork, goToWorkBody, planGoToWork } from './goToWork';
import type { Runner, RunnerPatchRequest } from '../types';

const NOW = new Date('2026-09-08T12:00:00Z');

const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();

const GONE_AFTER = 90;

const runner = (over: Partial<Runner> = {}): Runner => ({
  name: 'here:/checkouts/one',
  kind: 'loop',
  firstSeenAt: at(-3_600_000),
  lastSeenAt: at(0),
  claimKey: null,
  line: null,
  lineAt: null,
  state: 'running',
  under: null,
  repositories: [],
  tags: [],
  projects: [],
  drives: [],
  clones: null,
  mine: null,
  where: 'here:/checkouts/one',
  agent: null,
  maxRuns: null,
  maxSpend: null,
  untilAt: null,
  goneAfterSeconds: GONE_AFTER,
  exhaustedUntil: null,
  for: null,
  canControl: true,
  yours: true,
  order: null,
  revision: null,
  sequence: null,
  builtAt: null,
  boardDrift: 'Unknown',
  sourceDrift: 'Unknown',
  ...over,
});

const recorder = (refuse: string[] = []) => {
  const calls: [string, RunnerPatchRequest][] = [];
  const patch = async (name: string, body: RunnerPatchRequest) => {
    calls.push([name, body]);
    if (refuse.includes(name)) throw new Error(`${name} refused`);
    return runner({ name });
  };
  return { calls, patch };
};

describe('planGoToWork', () => {
  it('is loading until the runners read has answered', () => {
    expect(planGoToWork(null, NOW)).toEqual({ mode: 'loading' });
  });

  it('is none when the only running loop belongs to someone else, and sends nothing', async () => {
    const plan = planGoToWork([runner({ yours: false })], NOW);
    expect(plan).toEqual({ mode: 'none' });

    const { calls, patch } = recorder();
    await applyGoToWork(plan, '', patch);
    expect(calls).toEqual([]);
  });

  it('ignores a once row and a gone row', () => {
    const plan = planGoToWork(
      [
        runner({ name: 'once', kind: 'once' }),
        runner({ name: 'gone', lastSeenAt: at(-GONE_AFTER * 1000 - 1000) }),
      ],
      NOW,
    );
    expect(plan).toEqual({ mode: 'none' });
  });

  it('pauses when any is running, targeting only the running ones', () => {
    const run = runner({ name: 'run', state: 'running' });
    const rest = runner({ name: 'rest', state: 'paused' });
    const plan = planGoToWork([rest, run], NOW);
    expect(plan).toEqual({ mode: 'pause', targets: [run] });
  });

  it('still pauses a running row that is out of usage', () => {
    const out = runner({ name: 'out', exhaustedUntil: at(3_600_000) });
    expect(planGoToWork([out], NOW)).toEqual({ mode: 'pause', targets: [out] });
  });

  it('goes when all are paused, with the project filter in the body', async () => {
    const plan = planGoToWork(
      [runner({ name: 'a', state: 'paused' }), runner({ name: 'b', state: 'paused' })],
      NOW,
    );
    expect(plan.mode).toBe('go');

    const filtered = recorder();
    await applyGoToWork(plan, 'FIL', filtered.patch);
    expect(filtered.calls).toEqual([
      ['a', { state: 'running', projects: ['FIL'] }],
      ['b', { state: 'running', projects: ['FIL'] }],
    ]);

    const all = recorder();
    await applyGoToWork(plan, '', all.patch);
    expect(all.calls.map(([, b]) => b)).toEqual([
      { state: 'running', projects: [] },
      { state: 'running', projects: [] },
    ]);
  });

  it('is stopping when every live one is stopping', () => {
    expect(planGoToWork([runner({ state: 'stopping' })], NOW)).toEqual({ mode: 'stopping' });
  });

  it('goes past a stopping row and leaves it untouched', async () => {
    const plan = planGoToWork(
      [runner({ name: 'stop', state: 'stopping' }), runner({ name: 'rest', state: 'paused' })],
      NOW,
    );
    const { calls, patch } = recorder();
    await applyGoToWork(plan, '', patch);
    expect(plan.mode).toBe('go');
    expect(calls.map(([n]) => n)).toEqual(['rest']);
  });
});

describe('goToWorkBody', () => {
  it('pauses with a state alone', () => {
    expect(goToWorkBody('pause', 'FIL')).toEqual({ state: 'paused' });
  });

  it('is a fresh object each call', () => {
    expect(goToWorkBody('go', '')).not.toBe(goToWorkBody('go', ''));
  });
});

describe('applyGoToWork', () => {
  it('records identical bodies for two presses on the same plan', async () => {
    const plan = planGoToWork([runner({ name: 'a' }), runner({ name: 'b' })], NOW);
    const first = recorder();
    const second = recorder();
    await applyGoToWork(plan, 'FIL', first.patch);
    await applyGoToWork(plan, 'FIL', second.patch);
    expect(second.calls).toEqual(first.calls);
    expect(first.calls).toHaveLength(2);
  });

  it('tries every target when one is refused, then rejects with that message', async () => {
    const plan = planGoToWork(
      [runner({ name: 'a' }), runner({ name: 'b' }), runner({ name: 'c' })],
      NOW,
    );
    const { calls, patch } = recorder(['b']);
    await expect(applyGoToWork(plan, '', patch)).rejects.toThrow('b refused');
    expect(calls.map(([n]) => n)).toEqual(['a', 'b', 'c']);
  });

  it('does nothing for loading, none or stopping', async () => {
    const { calls, patch } = recorder();
    for (const mode of ['loading', 'none', 'stopping'] as const) await applyGoToWork({ mode }, '', patch);
    expect(calls).toEqual([]);
  });
});
