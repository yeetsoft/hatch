import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { RunnerList } from './RunnerList';
import type { Runner, RunnerSession } from '../types';

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
  models: [],
  has: [],
  drives: [],
  clones: null,
  mine: null,
  where: 'here:/checkouts/one',
  agent: null,
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
  goneAfterSeconds: GONE_AFTER,
  exhaustedUntil: null,
  for: null,
  canControl: true,
  yours: true,
  runs: null,
  spent: null,
  host: null,
  order: null,
  revision: null,
  sequence: null,
  builtAt: null,
  boardDrift: 'Unknown',
  sourceDrift: 'Unknown',
  apiKeyId: 'k1',
  ...over,
});

const session = (over: Partial<RunnerSession> = {}): RunnerSession => ({
  id: 1,
  where: 'here:/checkouts/one',
  kind: 'loop',
  host: null,
  state: 'running',
  startedAt: at(-3_600_000),
  lastSeenAt: at(0),
  endedAt: null,
  claimKey: null,
  agent: 'claude',
  os: 'Mac',
  exhaustedUntil: null,
  throttledUntil: null,
  ...over,
});

const live = (name: string, over: Partial<Runner> = {}) => runner({ name, sessions: [session()], ...over });

/** Seen a minute ago, so past nothing yet - then a second session ended long before it. */
const gone = (name: string, over: Partial<Runner> = {}) =>
  runner({ name, sessions: [session({ lastSeenAt: at(-3_600_000) })], lastSeenAt: at(-3_600_000), ...over });

interface Draw {
  runners: Runner[] | null;
  selected?: string;
  isAdmin?: boolean;
  wallOff?: boolean;
}

const draw = ({ runners, selected, isAdmin = false, wallOff = false }: Draw) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <RunnerList runners={runners} selected={selected} isAdmin={isAdmin} wallOff={wallOff} now={NOW} onCreate={() => {}} />
    </MemoryRouter>,
  );

describe('RunnerList', () => {
  it('draws a live runner before a gone one seen more recently', () => {
    const html = draw({
      runners: [gone('a:/gone', { lastSeenAt: at(-120_000), sessions: [session({ lastSeenAt: at(-120_000) })] }), live('b:/live', { sessions: [session({ lastSeenAt: at(-60_000) })] })],
    });

    expect(html.indexOf('b%3A%2Flive')).toBeGreaterThan(-1);
    expect(html.indexOf('b%3A%2Flive')).toBeLessThan(html.indexOf('a%3A%2Fgone'));
  });

  it('draws one glyph per row, each naming its state', () => {
    const html = draw({ runners: [live('a:/one'), gone('b:/two')] });

    expect(html.match(/role="img"/g)).toHaveLength(2);
    expect(html).toContain('data-state="idle"');
    expect(html).toContain('data-state="gone"');
  });

  it('has no details group without a revoked runner', () => {
    expect(draw({ runners: [live('a:/one')] })).not.toContain('<details');
  });

  it('collapses revoked runners into a group counting them', () => {
    const html = draw({ runners: [live('a:/one'), live('b:/two', { revokedAt: at(-1000) })] });

    expect(html).toContain('<summary>Revoked (1)</summary>');
    expect(html).toMatch(/<details(?![^>]*open)/);
    expect(html.indexOf('<details')).toBeGreaterThan(html.indexOf('a%3A%2Fone'));
    expect(html.indexOf('b%3A%2Ftwo')).toBeGreaterThan(html.indexOf('<details'));
  });

  it('opens the group exactly when the selected runner is in it', () => {
    const runners = [live('a:/one'), live('b:/two', { revokedAt: at(-1000) })];

    expect(draw({ runners, selected: 'b:/two' })).toMatch(/<details[^>]*open=""/);
    expect(draw({ runners, selected: 'a:/one' })).not.toMatch(/<details[^>]*open=""/);
    expect(draw({ runners })).not.toMatch(/<details[^>]*open=""/);
  });

  it('marks only the selected row as the current page', () => {
    const html = draw({ runners: [live('a:/one'), live('b:/two')], selected: 'b:/two' });

    expect(html.match(/aria-current="page"/g)).toHaveLength(1);
  });

  it('draws only the revoked group, with no empty list, when nothing else is shown', () => {
    const html = draw({ runners: [live('a:/one', { revokedAt: at(-1000) })] });

    expect(html).not.toContain('No runners yet');
    expect(html.match(/<ul/g)).toHaveLength(1);
  });

  it('does not draw a retired runner or one without a key', () => {
    const html = draw({
      runners: [live('a:/one'), live('b:/retired', { retiredAt: at(-1000) }), live('c:/keyless', { apiKeyId: null })],
    });

    expect(html).toContain('a%3A%2Fone');
    expect(html).not.toContain('b%3A%2Fretired');
    expect(html).not.toContain('c%3A%2Fkeyless');
  });

  it('draws nothing while the read is out', () => {
    expect(draw({ runners: null })).toBe('');
  });

  describe('with no runners', () => {
    it('tells an admin to create one, and offers to', () => {
      const html = draw({ runners: [], isAdmin: true });

      expect(html).toContain('No runners yet. A runner is a key: create one, then hand its secret to a checkout.');
      expect(html).toContain('Create runner');
    });

    it('tells anyone else to ask an admin, with no button', () => {
      const html = draw({ runners: [] });

      expect(html).toContain('No runners yet. Ask an admin to create one.');
      expect(html).not.toContain('Create runner');
    });

    it('tells the wall-off reader to run hatch config, with no button, even if isAdmin is set', () => {
      const html = draw({ runners: [], wallOff: true, isAdmin: true });

      expect(html).toContain('No runners yet. Run <code>hatch config</code> in a checkout and it makes its own.');
      expect(html).not.toContain('Create runner');
      expect(html).not.toContain('Ask an admin');
    });

    it('counts a list of only retired runners as empty', () => {
      expect(draw({ runners: [live('a:/one', { retiredAt: at(-1000) })] })).toContain('No runners yet.');
    });
  });
});
