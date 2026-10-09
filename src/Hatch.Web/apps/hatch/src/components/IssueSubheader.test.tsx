import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { IssueSubheader } from './IssueSubheader';
import type { Issue } from '../types';

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
});
