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
});
