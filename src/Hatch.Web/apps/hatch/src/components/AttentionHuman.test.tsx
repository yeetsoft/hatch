import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AttentionHuman } from './AttentionHuman';
import { approvedHeldEmptyWords, questionEmptyWords, reviewEmptyWords, trunkBuildEmptyWords } from '../lib/attention';
import type { Attention, ApprovedHeld, TrunkBuild } from '../types';

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

const approvedHeld = (over: Partial<ApprovedHeld> = {}): ApprovedHeld => ({
  key: 'AER-14',
  title: 'A story that stopped merging',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/14',
  sentence: 'Its build is failing.',
  ...over,
});

beforeEach(() => {
  vi.stubGlobal('window', { location: { pathname: '/apps/hatch/' } });
  dialogProps = null;
});
afterEach(() => vi.unstubAllGlobals());

describe('AttentionHuman', () => {
  it('draws trunk builds, then pull requests, then questions, each with the panel’s own empty wording', () => {
    const html = renderToStaticMarkup(<AttentionHuman attention={null} now={new Date()} reload={noop} />);

    const headings = [...html.matchAll(/<h3 class="hatch-attention-heading">([^<]*)<\/h3>/g)].map((m) => m[1]);
    expect(headings).toEqual([
      'Trunk builds that fail',
      'Pull requests to review',
      'Approved, not merging',
      'Questions to answer',
    ]);

    const trunkAt = html.indexOf(trunkBuildEmptyWords());
    const reviewAt = html.indexOf(reviewEmptyWords(null));
    const approvedHeldAt = html.indexOf(approvedHeldEmptyWords());
    const questionAt = html.indexOf(questionEmptyWords());

    expect(trunkAt).toBeGreaterThan(-1);
    expect(trunkAt).toBeLessThan(reviewAt);
    expect(reviewAt).toBeLessThan(approvedHeldAt);
    expect(approvedHeldAt).toBeLessThan(questionAt);
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
