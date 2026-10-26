import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { IssueSubheader } from './IssueSubheader';
import type { Issue } from '../types';

// IssueSubheader now reads window.location unconditionally (for the copy-link
// control's full address), the same as appHref and IssuePeek.tsx/
// Confirmations.tsx already do - so, same fix as Confirmations.test.tsx:16-22,
// this stubs window rather than guarding the component. Command.tsx reads the
// bare `navigator` identifier (not window.navigator), which in a real browser
// is the same object window.navigator is - so both are stubbed to the same
// value here to keep that true in the test too.
function stubWindow(pathname: string, navigator: Record<string, unknown> = {}) {
  vi.stubGlobal('window', { location: { pathname, origin: 'https://example.test' }, navigator });
  vi.stubGlobal('navigator', navigator);
}

beforeEach(() => stubWindow('/apps/hatch/'));
afterEach(() => vi.unstubAllGlobals());

const issue = (over: Partial<Issue> = {}): Issue => ({
  key: 'AER-1',
  projectId: 1,
  projectKey: 'AER',
  type: 'task',
  title: 'A task',
  description: '',
  statusId: 1,
  rank: 0,
  parentKey: null,
  childKeys: [],
  dependsOnKeys: [],
  dependentKeys: [],
  readyAt: null,
  dueAt: null,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
  releaseNote: null,
  modelOverride: null,
  effortOverride: null,
  assignee: null,
  createdBy: 'Pam Beesly',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
  runnerTags: [],
  runnerTagsOwn: [],
  runnerTagsFrom: [],
  dangerousOwn: null,
  dangerous: null,
  dangerousFrom: null,
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  stallResumeSeconds: 0,
  mergeChecks: [],
  buildChecks: [],
  wipLimit: null,
  ...over,
});

const render = (over: Partial<Issue> = {}) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <IssueSubheader issue={issue(over)} project={null} stopped={false} />
    </MemoryRouter>,
  );

describe('IssueSubheader', () => {
  it('links to the parent when the issue has one', () => {
    const html = render({ parentKey: 'AER-9' });

    expect(html).toContain('↳');
    expect(html).toContain('AER-9');
  });

  it('draws no parent slot at all when the issue has none', () => {
    const html = render({ parentKey: null });

    expect(html).not.toContain('↳');
  });

  it('draws the merge-hold chip with its sentence when held', () => {
    const html = render({ mergeWork: { kind: 'hold', sentence: 'its build on abc1234 failed' } });

    expect(html).toContain('its build on abc1234 failed');
  });

  it.each(['merge', 'update', 'pending'])('draws nothing for a %s judgement', (kind) => {
    const html = render({ mergeWork: { kind, sentence: 'should not appear' } });

    expect(html).not.toContain('should not appear');
  });

  it('draws nothing when there is no judgement at all', () => {
    const html = render({ mergeWork: undefined });

    expect(html).not.toContain('hatch-chip-merge-hold');
  });

  it('draws the forge\'s merge state and review decision beside the pull request', () => {
    const html = render({
      pullRequestUrl: 'https://forge.test/o/r/pull/1',
      pullRequestMergeState: 'dirty',
      pullRequestReviewDecision: 'approved',
    });

    expect(html).toContain('in conflict');
    expect(html).toContain('review: approved');
  });

  it('draws neither chip without a pull request', () => {
    const html = render({ pullRequestUrl: null, pullRequestMergeState: 'dirty', pullRequestReviewDecision: 'approved' });

    expect(html).not.toContain('in conflict');
    expect(html).not.toContain('review: approved');
  });

  it('builds the full address against the hatch. subdomain path shape', () => {
    stubWindow('/issues/HA-446');
    const html = render({ key: 'HA-446' });

    expect(html).toContain('https://example.test/issues/HA-446');
  });

  it('builds the full address against the house-hostname path shape', () => {
    stubWindow('/apps/hatch/issues/HA-446');
    const html = render({ key: 'HA-446' });

    expect(html).toContain('https://example.test/apps/hatch/issues/HA-446');
  });

  it('draws no copy button when there is no clipboard on navigator, but still shows the address', () => {
    stubWindow('/issues/HA-446', {});
    const html = render({ key: 'HA-446' });

    expect(html).toContain('https://example.test/issues/HA-446');
    expect(html).not.toContain('<button');
  });

  it("draws the 'Copy link' button when a clipboard is present", () => {
    stubWindow('/issues/HA-446', { clipboard: { writeText: async () => undefined } });
    const html = render({ key: 'HA-446' });

    expect(html).toContain('Copy link');
    expect(html).toContain('<button');
  });

  it('draws the playbook-file chip when a merge check names a path', () => {
    const html = render({
      mergeChecks: [
        {
          remote: 'git@example.test:hatch/hatch.git',
          canonical: 'example.test/hatch/hatch',
          trunk: 'main',
          trunkSha: '1'.repeat(40),
          verdict: 'clean',
          branch: 'aer-1-thing',
          branchSha: '2'.repeat(40),
          files: [],
          holdsTrunk: true,
          checkedAt: '2026-01-01T00:00:00Z',
          runner: 'box:/work/repo',
          checkedBy: 'runner',
          playbookFilePath: '.hatch/playbooks.yaml',
        },
      ],
    });

    expect(html).toContain('this change also rewrites the instructions agents follow - .hatch/playbooks.yaml');
  });

  it('draws no playbook-file chip when no merge check names a path', () => {
    const html = render({ mergeChecks: [] });

    expect(html).not.toContain('hatch-chip-playbook-file');
  });

  it('draws an unlinked chip per repository with a recorded landing', () => {
    const html = render({
      landings: [
        {
          remote: 'git@example.test:hatch/hatch.git',
          canonical: 'example.test/hatch/hatch',
          trunk: 'main',
          sha: 'abc1234def5678',
          containedBy: null,
          recordedAt: '2026-01-01T00:00:00Z',
          checkedAt: null,
        },
        {
          remote: 'git@example.test:hatch/web.git',
          canonical: 'example.test/hatch/web',
          trunk: 'trunk',
          sha: '9876543fedcba0',
          containedBy: null,
          recordedAt: '2026-01-01T00:00:00Z',
          checkedAt: null,
        },
      ],
    });

    expect(html).toContain('Landed on main at abc1234');
    expect(html).toContain('Landed on trunk at 9876543');
    expect(html).not.toContain('<a href');
  });
});
