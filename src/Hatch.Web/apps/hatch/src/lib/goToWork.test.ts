import { describe, expect, it } from 'vitest';
import { applyGoToWork, goToWorkBody, planGoToWork, statusLine } from './goToWork';
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
  claimStatus: null,
  line: null,
  lineAt: null,
  state: 'running',
  under: null,
  repositories: [],
  tags: [],
  projects: [],
  refusesDangerous: false,
  drives: [],
  clones: null,
  mine: null,
  where: 'here:/checkouts/one',
  agent: null,
  runs: null,
  spent: null,
  host: null,
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

describe('statusLine', () => {
  const quiet = { lastSeenAt: at(-(GONE_AFTER + 10) * 1000) };

  it('is empty until the runners read has answered', () => {
    expect(statusLine(null, NOW)).toEqual({ before: '', key: null, after: '', tone: 'muted', more: 0 });
  });

  it('says No runner when none of the rows are yours loops', () => {
    expect(statusLine([], NOW)).toEqual({ before: 'No runner', key: null, after: '', tone: 'muted', more: 0 });
    expect(statusLine([runner({ kind: 'once' }), runner({ yours: false })], NOW).before).toBe('No runner');
  });

  it('names the column of a working claim', () => {
    expect(statusLine([runner({ claimKey: 'AER-12', claimStatus: 'In Progress' })], NOW)).toEqual({
      before: 'Working ',
      key: 'AER-12',
      after: ' · In Progress',
      tone: 'success',
      more: 0,
    });
  });

  it('drops the column when the claim has none', () => {
    expect(statusLine([runner({ claimKey: 'AER-12' })], NOW).after).toBe('');
  });

  it('still reads working for a running row that is out of usage but holds a claim', () => {
    const line = statusLine([runner({ claimKey: 'AER-12', exhaustedUntil: at(3_600_000) })], NOW);
    expect(line.before).toBe('Working ');
  });

  it('reads pausing after the key for a paused row with a claim', () => {
    expect(statusLine([runner({ state: 'paused', claimKey: 'AER-12' })], NOW)).toEqual({
      before: 'Pausing after ',
      key: 'AER-12',
      after: '',
      tone: 'primary',
      more: 0,
    });
  });

  it('reads stopping after the key for a stopping row with a claim', () => {
    expect(statusLine([runner({ state: 'stopping', claimKey: 'AER-12' })], NOW)).toEqual({
      before: 'Stopping after ',
      key: 'AER-12',
      after: '',
      tone: 'primary',
      more: 0,
    });
  });

  it('reads out of usage for a row with no claim and a reset time ahead', () => {
    const line = statusLine([runner({ exhaustedUntil: at(3_600_000) })], NOW);
    expect(line.before).toMatch(/^Out of usage until /);
    expect(line).toMatchObject({ key: null, after: '', tone: 'danger' });
  });

  it('does not count an exhausted time in the past', () => {
    expect(statusLine([runner({ exhaustedUntil: at(-1000) })], NOW).before).toMatch(/^Idle/);
  });

  it('reads idle with the projects it is bound to', () => {
    expect(statusLine([runner({ projects: ['FIL'] })], NOW)).toMatchObject({
      before: 'Idle - nothing to do in FIL',
      tone: 'muted',
    });
    expect(statusLine([runner({ projects: ['FIL', 'HA'] })], NOW).before).toBe('Idle - nothing to do in FIL, HA');
    expect(statusLine([runner()], NOW).before).toBe('Idle - nothing to do in any project');
  });

  it('reads paused and stopping for rows with no claim', () => {
    expect(statusLine([runner({ state: 'paused' })], NOW)).toMatchObject({ before: 'Paused', tone: 'primary' });
    expect(statusLine([runner({ state: 'stopping' })], NOW)).toMatchObject({ before: 'Stopping', tone: 'primary' });
  });

  it('reads stopped unexpectedly for a gone row that was running, claim or not', () => {
    const expected = { before: 'Stopped unexpectedly', key: null, after: '', tone: 'danger', more: 0 };
    expect(statusLine([runner(quiet)], NOW)).toEqual(expected);
    expect(statusLine([runner({ ...quiet, claimKey: 'AER-12', claimStatus: 'In Progress' })], NOW)).toEqual(expected);
  });

  it('reads off for a gone row that was paused or stopping, whatever it holds', () => {
    const expected = { before: 'Off', key: null, after: '', tone: 'muted', more: 0 };
    expect(statusLine([runner({ ...quiet, state: 'paused' })], NOW)).toEqual(expected);
    expect(statusLine([runner({ ...quiet, state: 'stopping', claimKey: 'AER-12' })], NOW)).toEqual(expected);
  });

  it('names the working loop over an idle one and counts the other', () => {
    const idle = runner({ name: 'a' });
    const working = runner({ name: 'b', claimKey: 'AER-12', claimStatus: 'In Progress' });
    expect(statusLine([idle, working], NOW)).toMatchObject({ before: 'Working ', key: 'AER-12', more: 1 });
  });

  it('gives a tie to the first row', () => {
    const rows = [runner({ name: 'a', claimKey: 'AER-1' }), runner({ name: 'b', claimKey: 'AER-2' })];
    expect(statusLine(rows, NOW)).toMatchObject({ key: 'AER-1', more: 1 });
  });

  it('prefers a live row to a gone one', () => {
    const line = statusLine([runner({ ...quiet, name: 'a' }), runner({ name: 'b', state: 'paused' })], NOW);
    expect(line).toMatchObject({ before: 'Paused', more: 0 });
  });

  it('counts every other row when the winner is gone', () => {
    expect(statusLine([runner({ ...quiet, name: 'a' }), runner({ ...quiet, name: 'b' })], NOW).more).toBe(1);
  });

  it('ignores a once row and another person row', () => {
    const line = statusLine([runner({ kind: 'once', claimKey: 'AER-9' }), runner({ yours: false, claimKey: 'AER-8' }), runner()], NOW);
    expect(line).toMatchObject({ before: expect.stringMatching(/^Idle/), more: 0 });
  });
});
