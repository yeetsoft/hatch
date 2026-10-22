import { describe, expect, it, vi } from 'vitest';
import { fileReflection } from './projectReflect';
import type { Issue } from '../types';

const ISSUE: Issue = {
  key: 'AER-1',
  projectId: 1,
  projectKey: 'AER',
  type: 'bug',
  title: 'Reflection',
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

describe('fileReflection', () => {
  it('resolves ok with the filed issue on success', async () => {
    const post = vi.fn(async () => ISSUE);

    const result = await fileReflection(9, post);

    expect(post).toHaveBeenCalledWith(9);
    expect(result).toEqual({ ok: true, issue: ISSUE });
  });

  it('resolves, rather than rejects, with the server refusal', async () => {
    const post = vi.fn(async () => {
      throw new Error('not an owner of this project');
    });

    const result = await fileReflection(9, post);

    expect(result).toEqual({ ok: false, error: 'not an owner of this project' });
  });
});
