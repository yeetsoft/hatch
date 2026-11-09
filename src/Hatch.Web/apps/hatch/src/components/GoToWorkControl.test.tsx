import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { GoToWorkControl, GoToWorkNote } from './GoToWorkControl';
import { planGoToWork } from '../lib/goToWork';
import type { GoToWorkPlan, StatusLine } from '../lib/goToWork';
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
  refusesDangerous: false,
  models: [],
  has: [],
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
  maxTokens: null,
  maxTokensPerHour: null,
  maxTokensSource: 'none',
  maxTokensPerHourSource: 'none',
  tokensUsed: null,
  tokensLastHour: null,
  throttledUntil: null,
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

const EMPTY_LINE: StatusLine = { before: '', key: null, after: '', tone: 'muted', more: 0, verb: '' };
const WORKING: StatusLine = { before: 'Working ', key: 'FIL-97', after: ' · In Progress', tone: 'success', more: 0, verb: 'Working' };

const render = (plan: GoToWorkPlan, project = '', isPhone = false, status: StatusLine = EMPTY_LINE, scope: string[] = []) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <GoToWorkControl plan={plan} status={status} project={project} scope={scope} reload={async () => {}} isPhone={isPhone} />
    </MemoryRouter>,
  );

const NO_RUNNER: StatusLine = { before: 'No runner', key: null, after: '', tone: 'muted', more: 0, verb: 'No runner' };

const go = planGoToWork([runner({ state: 'paused' })], NOW);
const pause = planGoToWork([runner({ state: 'running' })], NOW);

describe('GoToWorkControl', () => {
  it('names the scope’s projects when the picker is empty', () => {
    const html = render(go, '', false, EMPTY_LINE, ['AER', 'FIL']);

    expect(html).toContain('title="Start your runners on AER, FIL"');
  });

  it('names the picker’s project over the scope', () => {
    const html = render(go, 'FIL', false, EMPTY_LINE, ['AER', 'FIL']);

    expect(html).toContain('title="Start your runners on FIL"');
  });

  it('is disabled and silent while the runners are loading', () => {
    const html = render({ mode: 'loading' });

    expect(html).toContain('disabled=""');
    expect(html).not.toContain('hatch-go-to-work-note');
  });

  it('is disabled with a way to start one when no runner of the viewer is on', () => {
    const html = render({ mode: 'none' }, '', false, NO_RUNNER);

    expect(html).toContain('disabled=""');
    expect(html).toContain('Start one at login');
    expect(html).toContain('href="/docs/the-runner#starting-it-at-login"');
    expect(html).not.toContain('No runner of yours is on.');
  });

  it('is disabled with no note while the viewer’s runner is stopping', () => {
    const html = render({ mode: 'stopping' });

    expect(html).toContain('disabled=""');
    expect(html).not.toContain('hatch-go-to-work-note');
  });

  it('draws only the status verb in the bar on desktop, the whole line in its tooltip and name', () => {
    const html = render(pause, '', false, WORKING);

    expect(html).toContain('>Working<');
    expect(html).toContain('title="Working FIL-97 · In Progress"');
    expect(html).toContain('aria-label="Working FIL-97 · In Progress"');
    expect(html).toContain('hatch-go-to-work-status--success');
  });

  it('keeps the ticket link and the column behind the verb, in the panel', () => {
    expect(render(pause, '', false, WORKING)).toContain('href="/issues/FIL-97"');
  });

  it('keeps the way to start one at login in the panel, not beside the button', () => {
    const html = render({ mode: 'none' }, '', false, NO_RUNNER);

    expect(html).toContain('Start one at login');
    expect(html).toContain('hatch-go-to-work-detail');
    expect(html).not.toMatch(/<\/button><span class="hatch-go-to-work-note/);
  });

  it('draws no status in the bar on a phone', () => {
    expect(render(pause, '', true, WORKING)).not.toContain('hatch-go-to-work-status');
    expect(render(pause, '', true, WORKING)).not.toContain('hatch-go-to-work-verb');
  });

  it('draws nothing for the empty status line', () => {
    expect(render(pause)).not.toContain('hatch-go-to-work-status');
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
    expect(render({ mode: 'none' }, '', true)).not.toContain('Start one at login');
  });
});

describe('GoToWorkNote', () => {
  it('is nothing for the modes that need no explaining', () => {
    for (const plan of [{ mode: 'loading' }, go, pause] as GoToWorkPlan[]) {
      expect(renderToStaticMarkup(<GoToWorkNote plan={plan} />)).toBe('');
    }
  });

  it('is nothing while stopping, which the status line now says', () => {
    expect(renderToStaticMarkup(<GoToWorkNote plan={{ mode: 'stopping' }} />)).toBe('');
  });
});
