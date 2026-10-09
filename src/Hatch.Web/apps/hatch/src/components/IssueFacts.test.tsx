import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { IssueFacts } from './IssueFacts';
import type { Issue, Project } from '../types';

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
  mergeChecks: [],
  wipLimit: null,
  ...over,
});

const project = (over: Partial<Project> = {}): Project => ({
  id: 1,
  key: 'AER',
  name: 'Aerial',
  issueCount: 1,
  createdAt: '2026-01-01T00:00:00Z',
  color: '#336699',
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  ...over,
});

const render = (i: Issue, p: Project | null = project(), stopped = false) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <IssueFacts issue={i} project={p} stopped={stopped} />
    </MemoryRouter>,
  );

describe('IssueFacts', () => {
  it('draws every field it has, and holds nothing that can write', () => {
    const html = render(
      issue({
        type: 'epic',
        parentKey: 'AER-0',
        assignee: { kind: 'person', id: 'p-1', name: 'Pam Beesly' },
        priority: 'expedited',
        priorityOwn: 'normal',
        priorityFrom: 'AER-0',
        express: true,
        readyAt: '2026-01-05T00:00:00Z',
        dueAt: '2026-01-10T00:00:00Z',
        pullRequestUrl: 'https://example.com/pr/1',
        modelOverride: 'claude-opus-5',
        effortOverride: 'high',
        wipLimit: 3,
      }),
    );

    // §6.2 / §6.3: nothing in read mode can write.
    expect(html).not.toContain('<select');
    expect(html).not.toContain('<input');
    expect(html).not.toContain('<button');

    expect(html).toContain('Expedited · inherited from AER-0');
    expect(html).toContain('Pam Beesly (person)');
    expect(html).toContain('claude-opus-5');
    expect(html).toContain('high');
    expect(html).toContain('3');
    expect(html).toContain('AER-0');
  });

  it('reads every empty field as an em dash, and drops the WIP row on a non-epic', () => {
    const html = render(issue());

    expect(html).not.toContain('<select');
    expect(html).not.toContain('<input');
    expect(html).not.toContain('<button');

    expect(html).toContain('—');
    expect(html).toContain('the playbook decides');
    expect(html).not.toContain('Stories at once');
  });
});
