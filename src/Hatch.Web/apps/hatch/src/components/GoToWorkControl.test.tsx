import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { GoToWorkControl, GoToWorkNote } from './GoToWorkControl';
import { planGoToWork } from '../lib/goToWork';
import type { GoToWorkPlan } from '../lib/goToWork';
import type { Runner } from '../types';

const NOW = new Date('2026-09-08T12:00:00Z');

const runner = (over: Partial<Runner> = {}): Runner => ({
  name: 'here:/checkouts/one',
  kind: 'loop',
  firstSeenAt: NOW.toISOString(),
  lastSeenAt: NOW.toISOString(),
  claimKey: null,
  claimStatus: null,
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
  runs: null,
  spent: null,
  host: null,
  maxRuns: null,
  maxSpend: null,
  untilAt: null,
  goneAfterSeconds: 90,
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

const render = (plan: GoToWorkPlan, project = '', isPhone = false) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <GoToWorkControl plan={plan} project={project} reload={async () => {}} isPhone={isPhone} />
    </MemoryRouter>,
  );

const go = planGoToWork([runner({ state: 'paused' })], NOW);
const pause = planGoToWork([runner({ state: 'running' })], NOW);

describe('GoToWorkControl', () => {
  it('is disabled and silent while the runners are loading', () => {
    const html = render({ mode: 'loading' });

    expect(html).toContain('disabled=""');
    expect(html).not.toContain('hatch-go-to-work-note');
  });

  it('is disabled with a way to start one when no runner of the viewer is on', () => {
    const html = render({ mode: 'none' });

    expect(html).toContain('disabled=""');
    expect(html).toContain('No runner of yours is on.');
    expect(html).toContain('href="/docs/the-runner#starting-it-at-login"');
  });

  it('is disabled and says so while the viewer’s runner is stopping', () => {
    const html = render({ mode: 'stopping' });

    expect(html).toContain('disabled=""');
    expect(html).toContain('Your runner is stopping.');
  });

  it('offers Go to Work, naming the project it will bound the start to', () => {
    const html = render(go, 'FIL');

    expect(html).toContain('>Go to Work<');
    expect(html).toContain('Start your runners on FIL');
    expect(html).not.toContain('disabled=""');
  });

  it('names every project when the picker is empty', () => {
    expect(render(go, '')).toContain('Start your runners on every project');
  });

  it('offers Pause, enabled', () => {
    const html = render(pause, 'FIL');

    expect(html).toContain('>Pause<');
    expect(html).toContain('each finishes the ticket in hand first');
    expect(html).not.toContain('disabled=""');
  });

  it('shortens go to Go on a phone, keeping the full words for a reader', () => {
    const html = render(go, '', true);

    expect(html).toContain('>Go<');
    expect(html).toContain('aria-label="Go to Work"');
  });

  it('keeps the note out of the bar on a phone', () => {
    expect(render({ mode: 'none' }, '', true)).not.toContain('No runner of yours is on.');
  });
});

describe('GoToWorkNote', () => {
  it('is nothing for the modes that need no explaining', () => {
    for (const plan of [{ mode: 'loading' }, go, pause] as GoToWorkPlan[]) {
      expect(renderToStaticMarkup(<GoToWorkNote plan={plan} />)).toBe('');
    }
  });
});
