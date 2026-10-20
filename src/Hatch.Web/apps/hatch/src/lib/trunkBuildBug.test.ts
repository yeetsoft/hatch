import { describe, expect, it, vi } from 'vitest';
import { bugRequest, defaultBugFields, fileBug, loadBugChoices } from './trunkBuildBug';
import type { BugFields } from './trunkBuildBug';
import type { AssigneeDirectory, Issue, Status } from '../types';

const status = (id: number, sortOrder: number, opts: Partial<Status> = {}): Status => ({
  id,
  name: `status-${id}`,
  sortOrder,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  color: '#6b7280',
  ...opts,
});

const TODO = status(1, 0);
const DOING = status(2, 1, { isImplementation: true });
const REVIEW = status(3, 2);
const DONE = status(4, 3, { isTerminal: true });
const BOARD = [TODO, DOING, REVIEW, DONE];

const ISSUE: Issue = {
  key: 'AER-1',
  projectId: 1,
  projectKey: 'AER',
  type: 'bug',
  title: 'A bug',
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
  modelOverride: null,
  effortOverride: null,
  assignee: null,
  createdBy: 'Britta Perry',
  createdAt: '2026-09-09T12:00:00Z',
  updatedAt: '2026-09-09T12:00:00Z',
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  stallResumeSeconds: 0,
  mergeChecks: [],
  buildChecks: [],
  wipLimit: null,
};

describe('defaultBugFields', () => {
  it('opens express, expedited, and sent back to the column that feeds implementation', () => {
    expect(defaultBugFields(BOARD)).toEqual({ express: true, priority: 'expedited', statusId: TODO.id });
  });

  it('has no column yet before the fetch resolves', () => {
    expect(defaultBugFields(null)).toEqual({ express: true, priority: 'expedited', statusId: null });
  });
});

describe('bugRequest', () => {
  it('sends exactly what a changed field shows', () => {
    const base: BugFields = { express: true, priority: 'expedited', statusId: TODO.id };

    expect(bugRequest({ ...base, express: false })).toEqual({ express: false, priority: 'expedited', statusId: TODO.id });
    expect(bugRequest({ ...base, priority: 'normal' })).toEqual({ express: true, priority: 'normal', statusId: TODO.id });
    expect(bugRequest({ ...base, statusId: 7 })).toEqual({ express: true, priority: 'expedited', statusId: 7 });
  });
});

describe('fileBug', () => {
  const fields: BugFields = { express: true, priority: 'expedited', statusId: TODO.id };

  it('resolves ok with the filed issue on success', async () => {
    const post = vi.fn(async () => ISSUE);

    const result = await fileBug(9, fields, post);

    expect(post).toHaveBeenCalledWith(9, bugRequest(fields));
    expect(result).toEqual({ ok: true, issue: ISSUE });
  });

  it('resolves, rather than rejects, with the server refusal', async () => {
    const post = vi.fn(async () => {
      throw new Error('no project bound to this repository');
    });

    const result = await fileBug(9, fields, post);

    expect(result).toEqual({ ok: false, error: 'no project bound to this repository' });
  });
});

describe('loadBugChoices', () => {
  const DIRECTORY: AssigneeDirectory = { me: null, assignees: [] };

  it('resolves ok with the columns and the directory', async () => {
    const result = await loadBugChoices(async () => BOARD, async () => DIRECTORY);

    expect(result).toEqual({ ok: true, statuses: BOARD, directory: DIRECTORY });
  });

  it('still resolves ok, with no directory, when only the directory read fails', async () => {
    const result = await loadBugChoices(
      async () => BOARD,
      async () => {
        throw new Error('GET /api/hatch/assignees failed: 500 Internal Server Error');
      },
    );

    expect(result).toEqual({ ok: true, statuses: BOARD, directory: null });
  });

  it('resolves, rather than rejects, with the sentence when the columns cannot be read', async () => {
    const result = await loadBugChoices(
      async () => {
        throw new Error('GET /api/hatch/statuses failed: 503 Service Unavailable');
      },
      async () => DIRECTORY,
    );

    expect(result).toEqual({ ok: false, error: 'GET /api/hatch/statuses failed: 503 Service Unavailable' });
  });
});
