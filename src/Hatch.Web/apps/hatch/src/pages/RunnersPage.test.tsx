import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { BoundsSummary, Row } from './RunnersPage';
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
  ...over,
});

const noop = (_request: RunnerPatchRequest) => undefined;

const renderRow = (r: Runner) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <table>
        <tbody>
          <Row runner={r} now={NOW} onPatch={noop} />
        </tbody>
      </table>
    </MemoryRouter>,
  );

describe('RunnersPage Row', () => {
  it('draws a working row\'s key as a link to the ticket, badged Working', () => {
    const html = renderRow(runner({ claimKey: 'HA-12' }));

    expect(html).toContain('href="/issues/HA-12"');
    expect(html).toContain('Working');
  });

  it('draws no ticket link for an idle row', () => {
    expect(renderRow(runner())).not.toContain('href="/issues/');
  });

  it('draws no ticket link for a paused row', () => {
    expect(renderRow(runner({ state: 'paused' }))).not.toContain('href="/issues/');
  });

  it('draws no ticket link for a stopping row', () => {
    expect(renderRow(runner({ state: 'stopping', claimKey: 'HA-12' }))).not.toContain('href="/issues/');
  });

  it('draws no ticket link for a gone row', () => {
    const html = renderRow(runner({ lastSeenAt: at(-3 * 3_600_000), claimKey: 'HA-12' }));
    expect(html).not.toContain('href="/issues/');
  });

  it('draws no ticket link for an exhausted row', () => {
    const html = renderRow(runner({ exhaustedUntil: at(3_600_000), claimKey: 'HA-12' }));
    expect(html).not.toContain('href="/issues/');
  });

  it('draws the controls on a loop row this caller can control', () => {
    const html = renderRow(runner({ canControl: true }));
    expect(html).toContain('Pause');
    expect(html).toContain('Stop after this one');
  });

  it('draws no controls and an owner sentence on a loop row this caller cannot control', () => {
    const html = renderRow(runner({ canControl: false, for: 'Some Person' }));
    expect(html).not.toContain('Pause');
    expect(html).not.toContain('Stop after this one');
    expect(html).not.toContain('Bounds');
    expect(html).toContain('Some Person');
  });

  it('draws no controls and no owner sentence on a once row', () => {
    const html = renderRow(runner({ kind: 'once' }));
    expect(html).not.toContain('Pause');
    expect(html).not.toContain('Stop after this one');
    expect(html).not.toContain('Bounds');
    expect(html).not.toContain('to control');
  });
});

const renderBounds = (r: Runner) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <BoundsSummary runner={r} />
    </MemoryRouter>,
  );

describe('RunnersPage BoundsSummary', () => {
  it('links the under epic to its ticket', () => {
    const html = renderBounds(runner({ under: 'HA-930' }));
    expect(html).toContain('href="/issues/HA-930"');
    expect(html).toContain('under');
  });

  it('draws no link when there is no under', () => {
    expect(renderBounds(runner())).not.toContain('href="/issues/');
  });

  it('joins under and other bounds with commas', () => {
    const html = renderBounds(runner({ under: 'HA-930', maxRuns: 5 }));
    expect(html).toContain('href="/issues/HA-930"');
    expect(html).toContain('5 run(s)');
    expect(html).toContain(', ');
  });
});
