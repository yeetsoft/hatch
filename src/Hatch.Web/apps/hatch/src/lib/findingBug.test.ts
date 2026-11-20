import { describe, expect, it, vi } from 'vitest';
import { fileFindingBug, findingBugRequest } from './findingBug';
import { bugRequest, defaultBugFields } from './trunkBuildBug';
import type { Issue } from '../types';

const FIELDS = { preApproval: 'none', priority: 'normal', statusId: 4 } as const;
const ISSUE = { key: 'AER-77' } as Issue;

describe('findingBugRequest', () => {
  it('carries the trunk build’s fields and the chosen project', () => {
    expect(findingBugRequest(FIELDS, 'AER')).toEqual({ ...bugRequest(FIELDS), projectKey: 'AER' });
  });

  it('carries no project for a finding that names its own', () => {
    expect(findingBugRequest(defaultBugFields(null), null).projectKey).toBeNull();
  });
});

describe('fileFindingBug', () => {
  it('posts once, to the finding, with the request, and returns the issue', async () => {
    const post = vi.fn().mockResolvedValue(ISSUE);

    const result = await fileFindingBug(12, FIELDS, 'AER', post);

    expect(result).toEqual({ ok: true, issue: ISSUE });
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith(12, findingBugRequest(FIELDS, 'AER'));
  });

  it('comes back as the refusal’s message, and does not post again', async () => {
    const post = vi.fn().mockRejectedValue(new Error('this finding names no project'));

    const result = await fileFindingBug(12, FIELDS, null, post);

    expect(result).toEqual({ ok: false, error: 'this finding names no project' });
    expect(post).toHaveBeenCalledTimes(1);
  });
});
