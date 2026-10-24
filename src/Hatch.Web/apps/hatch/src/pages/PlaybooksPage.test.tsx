import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PlaybooksTable } from './PlaybooksPage';
import type { Playbook, PlaybookFileDto, PlaybookFileRowDto, Project, Status } from '../types';

// PlaybooksPage pulls in PlaybookEditDialog, whose Prompt field's
// MarkdownEditor reaches lib/clientLogger, which reads `window` at import,
// and there is no jsdom here - the same stub PlaybookRow.test.tsx uses.
vi.mock('../components/MarkdownEditor', () => ({ MarkdownEditor: () => null }));
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const BASE: Playbook = {
  id: 1,
  fromStatusId: 1,
  fromStatusName: 'To Do',
  toStatusId: 2,
  toStatusName: 'In Progress',
  types: [],
  shape: 'any',
  prompt: 'Do the thing.',
  defaultPrompt: null,
  model: 'sonnet',
  effort: 'medium',
  budget: null,
  briefLimit: null,
  deniedTools: [],
  isCheck: false,
  updatedAt: '2026-01-01T00:00:00Z',
  projectId: null,
  projectKey: null,
  projectName: null,
};

const STATUSES: Status[] = [
  { id: 1, name: 'To Do', sortOrder: 1, isTerminal: false, isDeferred: false, isWip: false, expressSkips: false, parentPulls: false, agentFiles: false, isImplementation: false, isProtected: false, mergesPullRequest: false, runnerTags: [], color: '#888888' },
  { id: 2, name: 'In Progress', sortOrder: 2, isTerminal: false, isDeferred: false, isWip: false, expressSkips: false, parentPulls: false, agentFiles: false, isImplementation: false, isProtected: false, mergesPullRequest: false, runnerTags: [], color: '#888888' },
];

const TABLE_PROJECT: Project = {
  id: 1,
  key: 'AER',
  name: 'Aerospace',
  issueCount: 0,
  createdAt: '2026-01-01T00:00:00Z',
  color: null,
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  members: [],
  canApprove: false,
  canAdminister: false,
  landing: 'pull-request',
  checksRequired: true,
  onePullRequest: false,
  autoHealBugEnabled: false,
  autoHealBugPriority: 'emergency',
  autoHealBugPreApproval: 'express',
  autoHealBugStatusId: null,
  repoSourced: false,
  repoSourcedBy: null,
  repoSourcedAt: null,
  bindsTracker: false,
  bindsTrackerBy: null,
  bindsTrackerAt: null,
  reflectionCadenceDays: null,
  reflectionLastPeriodStart: null,
  reflectionNextDueAt: null,
  storiesOnEpics: false,
};

const REPO_PROJECT: Project = { ...TABLE_PROJECT, repoSourced: true };

const TWO_PROJECTS: Project[] = [TABLE_PROJECT, { ...TABLE_PROJECT, id: 2, key: 'HA', name: 'Hatch' }];

const noop = () => undefined;

function file(rows: PlaybookFileRowDto[]): PlaybookFileDto {
  return {
    id: 1,
    remote: 'git@example.com:acme/repo.git',
    canonical: 'acme/repo',
    trunk: 'main',
    sha: 'a1b2c3d4e5f6',
    state: 'parsed',
    hash: 'deadbeef',
    rows,
    problem: null,
    lastGoodSha: null,
    lastGoodHash: null,
    url: null,
    checkedAt: '2026-01-01T00:00:00Z',
    runner: 'runner-1',
    checkedBy: 'runner-1',
  };
}

describe('PlaybooksTable', () => {
  it('draws no toggle with no project filter', () => {
    const html = renderToStaticMarkup(
      <PlaybooksTable
        playbooks={[BASE]}
        activeFile={null}
        filteredProject={null}
        projects={TWO_PROJECTS}
        statuses={STATUSES}
        showOverridden={false}
        onToggleOverridden={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );
    expect(html).not.toContain('overridden');
  });

  it('AC1 & AC2: sets aside a board-wide row its own layer wholly overrides, and leaves an unrelated one alone', () => {
    const own: Playbook = { ...BASE, id: 2, projectKey: 'AER', projectName: 'Aerospace', projectId: 1 };
    const boardWide: Playbook = { ...BASE, id: 1 };
    const unrelated: Playbook = {
      ...BASE,
      id: 3,
      fromStatusId: 3,
      fromStatusName: 'Review',
      toStatusId: 4,
      toStatusName: 'Done',
    };
    const playbooks = [boardWide, own, unrelated];

    const hidden = renderToStaticMarkup(
      <PlaybooksTable
        playbooks={playbooks}
        activeFile={null}
        filteredProject={TABLE_PROJECT}
        projects={TWO_PROJECTS}
        statuses={STATUSES}
        showOverridden={false}
        onToggleOverridden={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );
    expect(hidden).toContain('Show 1 overridden');
    expect(hidden).not.toContain('hatch-playbook-row--dimmed');
    expect(hidden).toContain('Review');
    expect(hidden).toContain('Done');

    const shown = renderToStaticMarkup(
      <PlaybooksTable
        playbooks={playbooks}
        activeFile={null}
        filteredProject={TABLE_PROJECT}
        projects={TWO_PROJECTS}
        statuses={STATUSES}
        showOverridden
        onToggleOverridden={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );
    expect(shown).toContain('Hide 1 overridden');
    expect(shown).toContain('hatch-playbook-row--dimmed');
    expect(shown).toContain("overridden by AER&#x27;s own");
    expect(shown).toContain('Review');
    expect(shown).toContain('Done');
  });

  it('keeps a partly overridden board-wide row visible, worded with what is left, never dimmed', () => {
    const own: Playbook = { ...BASE, id: 2, projectKey: 'AER', projectName: 'Aerospace', projectId: 1, types: ['task'] };
    const boardWide: Playbook = { ...BASE, id: 1 };
    const playbooks = [boardWide, own];

    const html = renderToStaticMarkup(
      <PlaybooksTable
        playbooks={playbooks}
        activeFile={null}
        filteredProject={TABLE_PROJECT}
        projects={TWO_PROJECTS}
        statuses={STATUSES}
        showOverridden={false}
        onToggleOverridden={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );
    expect(html).not.toContain('overridden');
    expect(html).not.toContain('hatch-playbook-row--dimmed');
    expect(html).toContain('only');
  });

  it('AC5: a repo-sourced project sets aside the board-wide row its file overrides and its own leftover rows', () => {
    const boardWide: Playbook = { ...BASE, id: 1 };
    const leftover: Playbook = { ...BASE, id: 2, projectKey: 'AER', projectName: 'Aerospace', projectId: 1 };
    const playbooks = [boardWide, leftover];
    const activeFile = file([
      {
        fromStatusId: 1,
        fromStatusName: 'To Do',
        toStatusId: 2,
        toStatusName: 'In Progress',
        types: [],
        shape: 'any',
        prompt: 'From the file.',
        model: 'sonnet',
        effort: 'medium',
        budget: null,
        briefLimit: null,
        deniedTools: [],
        isCheck: false,
      },
    ]);

    const hidden = renderToStaticMarkup(
      <PlaybooksTable
        playbooks={playbooks}
        activeFile={activeFile}
        filteredProject={REPO_PROJECT}
        projects={TWO_PROJECTS}
        statuses={STATUSES}
        showOverridden={false}
        onToggleOverridden={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );
    expect(hidden).toContain('Show 2 overridden');
    expect(hidden).not.toContain('hatch-playbook-row--dimmed');

    const shown = renderToStaticMarkup(
      <PlaybooksTable
        playbooks={playbooks}
        activeFile={activeFile}
        filteredProject={REPO_PROJECT}
        projects={TWO_PROJECTS}
        statuses={STATUSES}
        showOverridden
        onToggleOverridden={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );
    expect(shown).toContain('Hide 2 overridden');
    expect(shown).toContain('overridden by the file');
    expect(shown).toContain('not read while AER reads its file');
  });
});
