import { describe, expect, it } from 'vitest';
import { openProjectDraft, projectDraftDiff, type ProjectDraft } from './projectDraft';

describe('openProjectDraft', () => {
  it('seeds known and the working fields from a Project', () => {
    const project = {
      id: 1,
      key: 'AER',
      name: 'Aerial',
      issueCount: 0,
      createdAt: '2026-01-01',
      color: null,
      icon: null,
      logoUpdatedAt: null,
      repositories: [{ remote: 'a', canonical: 'canonical-a', baseBranch: 'main' }],
    };

    const draft = openProjectDraft(project);

    expect(draft.name).toBe('Aerial');
    expect(draft.key).toBe('AER');
    expect(draft.known).toEqual({
      name: 'Aerial',
      key: 'AER',
      repositories: [{ remote: 'a', baseBranch: 'main' }],
    });
  });

  it('maps repositories down to remote and baseBranch, dropping canonical', () => {
    const project = {
      id: 1,
      key: 'AER',
      name: 'Aerial',
      issueCount: 0,
      createdAt: '2026-01-01',
      color: null,
      icon: null,
      logoUpdatedAt: null,
      repositories: [{ remote: 'a', canonical: 'canonical-a', baseBranch: null }],
    };

    const draft = openProjectDraft(project);

    expect(draft.repositories).toEqual([{ remote: 'a', baseBranch: null }]);
  });
});

describe('projectDraftDiff', () => {
  const known = { name: 'Aerial', key: 'AER', repositories: [{ remote: 'a', baseBranch: 'main' }] };

  it('is empty when nothing changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, repositories: known.repositories };
    expect(projectDraftDiff(draft)).toEqual({ patch: null, repositories: null });
  });

  it('carries only name when only name changed', () => {
    const draft: ProjectDraft = { known, name: 'Renamed', key: known.key, repositories: known.repositories };
    expect(projectDraftDiff(draft)).toEqual({ patch: { name: 'Renamed' }, repositories: null });
  });

  it('carries only key when only key changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: 'NEW', repositories: known.repositories };
    expect(projectDraftDiff(draft)).toEqual({ patch: { key: 'NEW' }, repositories: null });
  });

  it('reports a change when repositories are reordered despite identical membership', () => {
    const repositories = [
      { remote: 'a', baseBranch: 'main' },
      { remote: 'b', baseBranch: 'dev' },
    ];
    const reorderedKnown = { ...known, repositories };
    const draft: ProjectDraft = {
      known: reorderedKnown,
      name: reorderedKnown.name,
      key: reorderedKnown.key,
      repositories: [repositories[1], repositories[0]],
    };

    const diff = projectDraftDiff(draft);

    expect(diff.patch).toBeNull();
    expect(diff.repositories).toEqual([
      { remote: 'b', baseBranch: 'dev' },
      { remote: 'a', baseBranch: 'main' },
    ]);
  });

  it.each([
    {
      label: 'an added entry',
      repositories: [{ remote: 'a', baseBranch: 'main' }, { remote: 'b', baseBranch: null }],
    },
    { label: 'a removed entry', repositories: [] },
    { label: 'an edited baseBranch', repositories: [{ remote: 'a', baseBranch: 'develop' }] },
  ])('reports a change on $label', ({ repositories }) => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, repositories };

    const diff = projectDraftDiff(draft);

    expect(diff.patch).toBeNull();
    expect(diff.repositories).toEqual(repositories);
  });

  it('is unchanged when the working repositories are a freshly-built array with the same entries', () => {
    const draft: ProjectDraft = {
      known,
      name: known.name,
      key: known.key,
      repositories: known.repositories.map((r) => ({ ...r })),
    };

    expect(projectDraftDiff(draft).repositories).toBeNull();
  });
});
