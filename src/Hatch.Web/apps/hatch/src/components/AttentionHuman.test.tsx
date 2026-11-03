import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AttentionHuman } from './AttentionHuman';
import { approvedHeldEmptyWords, deadlockedEmptyWords, landingHeldEmptyWords, noCodeHeldEmptyWords, questionEmptyWords, reviewEmptyWords, trunkBuildEmptyWords } from '../lib/attention';
import type { Attention, ApprovedHeld, Deadlocked, LandingHeld, NoCodeHeld, Review, TrunkBuild } from '../types';

/* No DOM here, so this is structural - react-dom/server, the way
   Confirmations.test.tsx reads. What it pins is AC1's "same order, same
   empty wordings" against a refactor that might reorder the extraction by
   accident. */
const noop = () => Promise.resolve();

let dialogProps: {
  open: boolean;
  buildId: number;
  onClose: () => void;
  onFiled: (created: unknown) => void;
} | null = null;
vi.mock('./TrunkBuildBugDialog', () => ({
  TrunkBuildBugDialog: (props: typeof dialogProps) => {
    dialogProps = props;
    return null;
  },
}));

// Same six-field shape lib/attention.test.ts's own trunkBuild() builds - a
// test fixture, not imported.
const trunkBuild = (over: Partial<TrunkBuild> = {}): TrunkBuild => ({
  id: 7,
  remote: 'https://forge.example/owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  sha: '1111111111111111111111111111111111111111',
  shaSince: new Date().toISOString(),
  verdict: 'failed',
  failing: [{ name: 'CI', url: null }],
  checkedAt: new Date().toISOString(),
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  bugIssueKey: null,
  ...over,
});

const attention = (over: Partial<Attention> = {}): Attention => ({
  reviews: [],
  inReviewWithoutPullRequest: 0,
  questions: [],
  conflicts: [],
  reviewsHeldBack: 0,
  ...over,
});

const review = (over: Partial<Review> = {}): Review => ({
  key: 'AER-16',
  title: 'A story up for review',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/16',
  buildState: 'unknown',
  holdsTrunk: null,
  trunk: null,
  playbookFilePath: null,
  ...over,
});

const approvedHeld = (over: Partial<ApprovedHeld> = {}): ApprovedHeld => ({
  key: 'AER-14',
  title: 'A story that stopped merging',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/14',
  sentence: 'Its build is failing.',
  ...over,
});

const landingHeld = (over: Partial<LandingHeld> = {}): LandingHeld => ({
  key: 'AER-15',
  title: 'A story that landed and is not done',
  type: 'story',
  sentence: 'Its pull request merged but the issue is not done.',
  ...over,
});

const noCodeHeld = (over: Partial<NoCodeHeld> = {}): NoCodeHeld => ({
  key: 'AER-18',
  title: 'A task that needed no code',
  type: 'task',
  reason: 'The setting was already off.',
  ...over,
});

const deadlocked = (over: Partial<Deadlocked> = {}): Deadlocked => ({
  key: 'AER-16',
  title: 'A story stuck in a ring',
  type: 'story',
  sentence: 'deadlocked - it waits on AER-17, which waits on it.',
  ...over,
});

beforeEach(() => {
  vi.stubGlobal('window', { location: { pathname: '/apps/hatch/' } });
  dialogProps = null;
});
afterEach(() => vi.unstubAllGlobals());

describe('AttentionHuman', () => {
  it('draws deadlocked first, then trunk builds, then pull requests, then questions, each with the panel’s own empty wording', () => {
    const html = renderToStaticMarkup(<AttentionHuman attention={null} now={new Date()} reload={noop} />);

    const headings = [...html.matchAll(/<h3 class="hatch-attention-heading">([^<]*)<\/h3>/g)].map((m) => m[1]);
    expect(headings).toEqual([
      'Deadlocked',
      'Trunk builds that fail',
      'Pull requests to review',
      'Approved, not merging',
      'Landed, not done',
      'Finished with no code',
      'Questions to answer',
    ]);

    const deadlockedAt = html.indexOf(deadlockedEmptyWords());
    const trunkAt = html.indexOf(trunkBuildEmptyWords());
    const reviewAt = html.indexOf(reviewEmptyWords(null));
    const approvedHeldAt = html.indexOf(approvedHeldEmptyWords());
    const landingHeldAt = html.indexOf(landingHeldEmptyWords());
    const noCodeHeldAt = html.indexOf(noCodeHeldEmptyWords());
    const questionAt = html.indexOf(questionEmptyWords());

    expect(deadlockedAt).toBeGreaterThan(-1);
    expect(deadlockedAt).toBeLessThan(trunkAt);
    expect(trunkAt).toBeLessThan(reviewAt);
    expect(reviewAt).toBeLessThan(approvedHeldAt);
    expect(approvedHeldAt).toBeLessThan(landingHeldAt);
    expect(landingHeldAt).toBeLessThan(noCodeHeldAt);
    expect(noCodeHeldAt).toBeLessThan(questionAt);
  });

  it('draws Backups first, above Deadlocked, with the sentence, the last success and a link to Settings', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman
          attention={attention({ backup: { sentence: 'The last backup failed.', lastSuccessfulAt: '2026-09-09T12:00:00Z' } })}
          now={new Date()}
          reload={noop}
        />
      </MemoryRouter>,
    );

    const headings = [...html.matchAll(/<h3 class="hatch-attention-heading">([^<]*)<\/h3>/g)].map((m) => m[1]);
    expect(headings[0]).toBe('Backups');
    expect(headings[1]).toBe('Deadlocked');
    expect(html.indexOf('>Backups</h3>')).toBeLessThan(html.indexOf('>Deadlocked</h3>'));
    expect(html).toContain('The last backup failed.');
    expect(html).toContain('Last successful backup:');
    expect(html).toContain('href="/settings"');
  });

  it('says a backup that never succeeded has never succeeded', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman
          attention={attention({ backup: { sentence: 'The last backup failed.', lastSuccessfulAt: null } })}
          now={new Date()}
          reload={noop}
        />
      </MemoryRouter>,
    );

    expect(html).toContain('There has never been a successful backup.');
  });

  it('draws no Backups section for a null backup or an absent one', () => {
    for (const over of [{ backup: null }, {}]) {
      const html = renderToStaticMarkup(
        <MemoryRouter>
          <AttentionHuman attention={attention(over)} now={new Date()} reload={noop} />
        </MemoryRouter>,
      );

      expect(html).not.toContain('Backups');
      expect(html).not.toContain('href="/settings"');
    }
  });

  it('shows the empty words when nothing is deadlocked', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ deadlocked: [] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).toContain(deadlockedEmptyWords());
  });

  it('lists a deadlocked row’s key, title and sentence under Deadlocked, linked to the issue', () => {
    const row = deadlocked();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ deadlocked: [row] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(deadlockedEmptyWords());
    expect(html).toContain('>Deadlocked</h3>');
    expect(html).toContain(row.key);
    expect(html).toContain(row.title);
    expect(html).toContain(row.sentence);
    expect(html).toContain(`href="/issues/${row.key}"`);
  });

  it('shows the empty words when nothing landed has stopped short of done', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ landingHeld: [] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).toContain(landingHeldEmptyWords());
  });

  it('lists a landed row’s key, title and sentence under Landed, not done, linked to the issue', () => {
    const held = landingHeld();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ landingHeld: [held] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(landingHeldEmptyWords());
    expect(html).toContain(held.key);
    expect(html).toContain(held.title);
    expect(html).toContain(held.sentence);
    expect(html).toContain(`href="/issues/${held.key}"`);
  });

  it('shows the empty words when nothing finished with no code is waiting', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ noCodeHeld: [] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).toContain(noCodeHeldEmptyWords());
  });

  it('lists a no-code row’s key, title and reason under Finished with no code, linked to the issue', () => {
    const held = noCodeHeld();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ noCodeHeld: [held] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(noCodeHeldEmptyWords());
    expect(html).toContain(held.key);
    expect(html).toContain(held.title);
    expect(html).toContain(held.reason);
    expect(html).toContain(`href="/issues/${held.key}"`);
  });

  it('shows the empty words when nothing approved has stopped merging', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ approvedHeld: [] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).toContain(approvedHeldEmptyWords());
  });

  it('lists a held row’s key, title and sentence under Approved, not merging', () => {
    const held = approvedHeld();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ approvedHeld: [held] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(approvedHeldEmptyWords());
    expect(html).toContain(held.key);
    expect(html).toContain(held.title);
    expect(html).toContain(held.sentence);
  });

  it('says the playbook-file sentence for a review row that carries one', () => {
    const r = review({ playbookFilePath: '.hatch/playbooks.yaml' });

    const html = renderToStaticMarkup(
      <AttentionHuman attention={attention({ reviews: [r] })} now={new Date()} reload={noop} />,
    );

    expect(html).toContain('this change also rewrites the instructions agents follow - .hatch/playbooks.yaml');
  });

  it('draws no playbook-file sentence for a review row without one', () => {
    const r = review({ playbookFilePath: null });

    const html = renderToStaticMarkup(
      <AttentionHuman attention={attention({ reviews: [r] })} now={new Date()} reload={noop} />,
    );

    expect(html).not.toContain('this change also rewrites the instructions agents follow');
  });

  it('draws no outer heading and no .hatch-attention-group wrapper of its own', () => {
    const html = renderToStaticMarkup(<AttentionHuman attention={null} now={new Date()} reload={noop} />);

    expect(html).not.toContain('hatch-attention-group');
    expect(html).not.toContain('<h2');
  });

  it('wires a failing build’s row to its own dialog, closed until the press, filing nothing itself', () => {
    const build = trunkBuild();
    const reload = vi.fn(() => Promise.resolve());

    const html = renderToStaticMarkup(
      <AttentionHuman attention={attention({ trunkBuilds: [build] })} now={new Date()} reload={reload} />,
    );

    expect(html).toContain('File a bug');
    expect(html).not.toContain('hatch-attention-row-error');

    expect(dialogProps).toMatchObject({ open: false, buildId: build.id });
    expect(typeof dialogProps?.onClose).toBe('function');
    expect(typeof dialogProps?.onFiled).toBe('function');

    // TrunkBuildRow owns none of the filing or confirming itself - the only
    // thing its onFiled does is what the row was already doing on every other
    // reload, forwarded straight to the dialog.
    dialogProps?.onFiled({});
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('skips the button for a build that already has its bug, without a second dialog press', () => {
    const build = trunkBuild({ bugIssueKey: 'HA-1' });

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ trunkBuilds: [build] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain('File a bug');
    expect(html).toContain('HA-1');
    expect(dialogProps).toMatchObject({ open: false, buildId: build.id });
  });
});
