import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { RunnerFailuresView } from './RunnerFailures';
import { resetClockWords } from '../lib/attention';
import type { RunnerFailure } from '../types';

const NOW = new Date('2026-09-08T12:00:00Z');
const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();
const clock = (iso: string) => resetClockWords(new Date(iso), NOW);

const row = (kind: string, over: Partial<RunnerFailure> = {}): RunnerFailure => ({
  kind,
  where: null,
  why: null,
  beganAt: at(-3_600_000),
  lastReportedAt: at(0),
  endedAt: null,
  ...over,
});

const draw = (current: RunnerFailure[], earlier: RunnerFailure[]) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <RunnerFailuresView current={current} earlier={earlier} now={NOW} />
    </MemoryRouter>,
  );

describe('RunnerFailuresView', () => {
  it('draws a Failing list alone, with the reason where the board kept one', () => {
    const html = draw([row('forge', { why: 'token expired' })], []);
    expect(html).toContain('>Failing</h2>');
    expect(html).toContain('hatch-runner-failing');
    expect(html).toContain(`Cannot reach its forge since ${clock(at(-3_600_000))} - gh said: token expired`);
    expect(html).not.toContain('Earlier');
    expect(html).not.toContain('hatch-runner-earlier');
  });

  it('draws an Earlier list alone', () => {
    const html = draw([], [row('signing', { endedAt: at(-1_800_000) })]);
    expect(html).toContain('>Earlier</h2>');
    expect(html).toContain('hatch-runner-earlier');
    expect(html).toContain(
      `Cannot sign its commits - began ${clock(at(-3_600_000))}, ended ${clock(at(-1_800_000))}`,
    );
    expect(html).not.toContain('Failing');
    expect(html).not.toContain('hatch-runner-failing');
  });

  it('draws both, Failing above Earlier', () => {
    const html = draw([row('forge')], [row('signing', { endedAt: at(-1_800_000) })]);
    expect(html).toContain('>Failing</h2>');
    expect(html).toContain('>Earlier</h2>');
    expect(html.indexOf('Failing')).toBeLessThan(html.indexOf('Earlier'));
  });

  it('lists the tickets a failure cost, as links, in both lists', () => {
    const html = draw(
      [row('agent-signed-out', { tickets: ['HA-1', 'HA-2'] })],
      [row('signing', { endedAt: at(-1_800_000), tickets: ['HA-3'] })],
    );
    expect(html).toContain('href="/issues/HA-1" data-discover="true">HA-1</a>');
    expect(html).toContain('href="/issues/HA-2" data-discover="true">HA-2</a>');
    expect(html).toContain('href="/issues/HA-3" data-discover="true">HA-3</a>');
    expect(html.indexOf('HA-1')).toBeLessThan(html.indexOf('Earlier'));
    expect(html.indexOf('HA-3')).toBeGreaterThan(html.indexOf('Earlier'));
  });

  it('draws no ticket row where tickets are absent or empty', () => {
    expect(draw([row('forge')], [row('signing', { endedAt: at(-1_800_000), tickets: [] })])).not.toContain('hatch-runner-failure-tickets');
  });

  it('draws nothing for a runner with neither', () => {
    expect(draw([], [])).toBe('');
  });

  it('names the checkout when two rows in a list share a kind', () => {
    const html = draw([row('forge', { where: 'here:/one' }), row('forge', { where: 'here:/two' })], []);
    expect(html).toContain('at here:/one since');
    expect(html).toContain('at here:/two since');
    expect(draw([row('forge', { where: 'here:/one' })], [])).not.toContain('here:/one');
  });
});
