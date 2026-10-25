import { describe, expect, it } from 'vitest';
import {
  openProjectDraft,
  projectBindsTrackerDiff,
  projectCheckApprovesMergeDiff,
  projectStoriesOnEpicsDiff,
  projectDraftDiff,
  projectDraftKeyObjection,
  projectLandingDiff,
  projectReflectionDiff,
  projectRepoSourcedDiff,
  projectRunnerTagsDiff,
  type ProjectDraft,
} from './projectDraft';

describe('openProjectDraft', () => {
  it('seeds known and the working fields from a Project', () => {
    const project = {
      id: 1,
      key: 'AER',
      name: 'Aerial',
      issueCount: 0,
      createdAt: '2026-01-01',
      color: '#6b7280',
      icon: 'rocket',
      logoUpdatedAt: null,
      repositories: [{ remote: 'a', canonical: 'canonical-a', baseBranch: 'main' }],
      members: [],
      canApprove: false,
      canAdminister: false,
      landing: 'pull-request' as const,
      checksRequired: true,
      onePullRequest: false,
      autoHealBugEnabled: false,
      autoHealBugPriority: 'emergency' as const,
      autoHealBugPreApproval: 'express' as const,
      autoHealBugStatusId: null,
      repoSourced: false,
      repoSourcedBy: null,
      repoSourcedAt: null,
      bindsTracker: false,
      storiesOnEpics: true,
      checkApprovesMerge: true,
      checkApprovesMergeBy: null,
      checkApprovesMergeAt: null,
      bindsTrackerBy: null,
      bindsTrackerAt: null,
      reflectionCadenceDays: null,
      reflectionLastPeriodStart: null,
      reflectionNextDueAt: null,
    };

    const draft = openProjectDraft(project);

    expect(draft.name).toBe('Aerial');
    expect(draft.key).toBe('AER');
    expect(draft.checkApprovesMerge).toBe(true);
    expect(draft.known).toEqual({
      name: 'Aerial',
      key: 'AER',
      color: '#6b7280',
      icon: 'rocket',
      repositories: [{ remote: 'a', baseBranch: 'main' }],
      landing: 'pull-request',
      checksRequired: true,
      onePullRequest: false,
      autoHealBugEnabled: false,
      autoHealBugPriority: 'emergency',
      autoHealBugPreApproval: 'express',
      autoHealBugStatusId: null,
      repoSourced: false,
      bindsTracker: false,
      storiesOnEpics: true,
      checkApprovesMerge: true,
      runnerTags: [],
      reflectionCadenceDays: null,
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
      members: [],
      canApprove: false,
      canAdminister: false,
      landing: 'pull-request' as const,
      checksRequired: true,
      onePullRequest: false,
      autoHealBugEnabled: false,
      autoHealBugPriority: 'emergency' as const,
      autoHealBugPreApproval: 'express' as const,
      autoHealBugStatusId: null,
      repoSourced: false,
      repoSourcedBy: null,
      repoSourcedAt: null,
      bindsTracker: false,
      storiesOnEpics: false,
      checkApprovesMerge: false,
      checkApprovesMergeBy: null,
      checkApprovesMergeAt: null,
      bindsTrackerBy: null,
      bindsTrackerAt: null,
      reflectionCadenceDays: null,
      reflectionLastPeriodStart: null,
      reflectionNextDueAt: null,
    };

    const draft = openProjectDraft(project);

    expect(draft.repositories).toEqual([{ remote: 'a', baseBranch: null }]);
  });
});

describe('projectDraftDiff', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: false,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: [],
    reflectionCadenceDays: null,
  };

  it('is empty when nothing changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectDraftDiff(draft)).toEqual({ patch: null, repositories: null });
  });

  it('carries only name when only name changed', () => {
    const draft: ProjectDraft = { known, name: 'Renamed', key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectDraftDiff(draft)).toEqual({ patch: { name: 'Renamed' }, repositories: null });
  });

  it('carries only key when only key changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: 'NEW', color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectDraftDiff(draft)).toEqual({ patch: { key: 'NEW' }, repositories: null });
  });

  it('carries only color when only color changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: '#111827', icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectDraftDiff(draft)).toEqual({ patch: { color: '#111827' }, repositories: null });
  });

  it('carries only icon when only icon changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: 'comet', repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectDraftDiff(draft)).toEqual({ patch: { icon: 'comet' }, repositories: null });
  });

  it('sends an empty string to clear color', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: null, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectDraftDiff(draft)).toEqual({ patch: { color: '' }, repositories: null });
  });

  it('sends an empty string to clear icon', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: null, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectDraftDiff(draft)).toEqual({ patch: { icon: '' }, repositories: null });
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
      color: reorderedKnown.color,
      icon: reorderedKnown.icon,
      repositories: [repositories[1], repositories[0]],
      landing: reorderedKnown.landing,
      checksRequired: reorderedKnown.checksRequired,
      onePullRequest: false,
      autoHealBugEnabled: reorderedKnown.autoHealBugEnabled,
      autoHealBugPriority: reorderedKnown.autoHealBugPriority,
      autoHealBugPreApproval: reorderedKnown.autoHealBugPreApproval,
      autoHealBugStatusId: reorderedKnown.autoHealBugStatusId,
      repoSourced: reorderedKnown.repoSourced,
      bindsTracker: reorderedKnown.bindsTracker,
      storiesOnEpics: reorderedKnown.storiesOnEpics,
      checkApprovesMerge: reorderedKnown.checkApprovesMerge,
      runnerTags: '',
      reflectionCadenceDays: '',
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
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };

    const diff = projectDraftDiff(draft);

    expect(diff.patch).toBeNull();
    expect(diff.repositories).toEqual(repositories);
  });

  it('is unchanged when the working repositories are a freshly-built array with the same entries', () => {
    const draft: ProjectDraft = {
      known,
      name: known.name,
      key: known.key,
      color: known.color,
      icon: known.icon,
      repositories: known.repositories.map((r) => ({ ...r })),
      landing: known.landing,
      checksRequired: known.checksRequired,
      onePullRequest: false,
      autoHealBugEnabled: known.autoHealBugEnabled,
      autoHealBugPriority: known.autoHealBugPriority,
      autoHealBugPreApproval: known.autoHealBugPreApproval,
      autoHealBugStatusId: known.autoHealBugStatusId,
      repoSourced: known.repoSourced,
      bindsTracker: known.bindsTracker,
      storiesOnEpics: known.storiesOnEpics,
      checkApprovesMerge: known.checkApprovesMerge,
      runnerTags: '',
      reflectionCadenceDays: '',
    };

    expect(projectDraftDiff(draft).repositories).toBeNull();
  });
});

describe('projectLandingDiff', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: true,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: [],
    reflectionCadenceDays: null,
  };

  it('is null when nothing changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toBeNull();
  });

  it('carries only landing when only landing changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: 'trunk', checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ landing: 'trunk' });
  });

  it('carries only checksRequired when only checksRequired changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: false, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ checksRequired: false });
  });

  it('carries both when both changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: 'trunk', checksRequired: false, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ landing: 'trunk', checksRequired: false });
  });

  it('carries only onePullRequest when only onePullRequest changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: true, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ onePullRequest: true });
  });

  it('carries only autoHealBugEnabled when only autoHealBugEnabled changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: false, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ autoHealBugEnabled: false });
  });

  it('carries only autoHealBugPriority when only autoHealBugPriority changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: 'expedited', autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ autoHealBugPriority: 'expedited' });
  });

  it('carries only autoHealBugPreApproval when only autoHealBugPreApproval changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: 'preapproved', autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ autoHealBugPreApproval: 'preapproved' });
  });

  it('carries only autoHealBugStatusId as a string when only autoHealBugStatusId changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: 5, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ autoHealBugStatusId: '5' });
  });

  it('clears autoHealBugStatusId with an empty string when set back to automatic', () => {
    const withColumn = { ...known, autoHealBugStatusId: 5 };
    const draft: ProjectDraft = { known: withColumn, name: withColumn.name, key: withColumn.key, color: withColumn.color, icon: withColumn.icon, repositories: withColumn.repositories, landing: withColumn.landing, checksRequired: withColumn.checksRequired, onePullRequest: withColumn.onePullRequest, autoHealBugEnabled: withColumn.autoHealBugEnabled, autoHealBugPriority: withColumn.autoHealBugPriority, autoHealBugPreApproval: withColumn.autoHealBugPreApproval, autoHealBugStatusId: null, repoSourced: withColumn.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectLandingDiff(draft)).toEqual({ autoHealBugStatusId: '' });
  });

  it('ignores autoHealBugPriority, autoHealBugPreApproval and autoHealBugStatusId while the switch is off', () => {
    const off = { ...known, autoHealBugEnabled: false };
    const draft: ProjectDraft = {
      known: off,
      name: off.name,
      key: off.key,
      color: off.color,
      icon: off.icon,
      repositories: off.repositories,
      landing: off.landing,
      checksRequired: off.checksRequired,
      onePullRequest: off.onePullRequest,
      autoHealBugEnabled: false,
      autoHealBugPriority: 'expedited',
      autoHealBugPreApproval: 'preapproved',
      autoHealBugStatusId: 5,
      repoSourced: off.repoSourced,
      bindsTracker: off.bindsTracker,
      storiesOnEpics: off.storiesOnEpics,
      checkApprovesMerge: off.checkApprovesMerge,
      runnerTags: '',
      reflectionCadenceDays: '',
    };
    expect(projectLandingDiff(draft)).toBeNull();
  });
});

describe('projectRepoSourcedDiff', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: true,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: [],
    reflectionCadenceDays: null,
  };

  it('is null when nothing changed', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectRepoSourcedDiff(draft)).toBeNull();
  });

  it('carries repoSourced when it changed from false to true', () => {
    const draft: ProjectDraft = { known, name: known.name, key: known.key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: true, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectRepoSourcedDiff(draft)).toEqual({ repoSourced: true });
  });

  it('carries repoSourced when it changed from true to false', () => {
    const trueKnown = { ...known, repoSourced: true };
    const draft: ProjectDraft = { known: trueKnown, name: trueKnown.name, key: trueKnown.key, color: trueKnown.color, icon: trueKnown.icon, repositories: trueKnown.repositories, landing: trueKnown.landing, checksRequired: trueKnown.checksRequired, onePullRequest: trueKnown.onePullRequest, autoHealBugEnabled: trueKnown.autoHealBugEnabled, autoHealBugPriority: trueKnown.autoHealBugPriority, autoHealBugPreApproval: trueKnown.autoHealBugPreApproval, autoHealBugStatusId: trueKnown.autoHealBugStatusId, repoSourced: false, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' };
    expect(projectRepoSourcedDiff(draft)).toEqual({ repoSourced: false });
  });
});

describe('projectCheckApprovesMergeDiff', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: true,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: [],
    reflectionCadenceDays: null,
  };
  const draftOf = (checkApprovesMerge: boolean, was = false): ProjectDraft => ({
    known: { ...known, checkApprovesMerge: was },
    name: known.name,
    key: known.key,
    color: known.color,
    icon: known.icon,
    repositories: known.repositories,
    landing: known.landing,
    checksRequired: known.checksRequired,
    onePullRequest: known.onePullRequest,
    autoHealBugEnabled: known.autoHealBugEnabled,
    autoHealBugPriority: known.autoHealBugPriority,
    autoHealBugPreApproval: known.autoHealBugPreApproval,
    autoHealBugStatusId: known.autoHealBugStatusId,
    repoSourced: known.repoSourced,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge,
    runnerTags: '',
    reflectionCadenceDays: '',
  });

  it('is null when nothing changed', () => {
    expect(projectCheckApprovesMergeDiff(draftOf(false))).toBeNull();
  });

  it('carries checkApprovesMerge when it was ticked', () => {
    expect(projectCheckApprovesMergeDiff(draftOf(true))).toEqual({ checkApprovesMerge: true });
  });

  it('carries checkApprovesMerge when a known-true project was unticked', () => {
    expect(projectCheckApprovesMergeDiff(draftOf(false, true))).toEqual({ checkApprovesMerge: false });
  });
});

describe('projectBindsTrackerDiff', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: true,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: [],
    reflectionCadenceDays: null,
  };
  const draftOf = (bindsTracker: boolean): ProjectDraft => ({
    known,
    name: known.name,
    key: known.key,
    color: known.color,
    icon: known.icon,
    repositories: known.repositories,
    landing: known.landing,
    checksRequired: known.checksRequired,
    onePullRequest: known.onePullRequest,
    autoHealBugEnabled: known.autoHealBugEnabled,
    autoHealBugPriority: known.autoHealBugPriority,
    autoHealBugPreApproval: known.autoHealBugPreApproval,
    autoHealBugStatusId: known.autoHealBugStatusId,
    repoSourced: known.repoSourced,
    bindsTracker,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: '',
    reflectionCadenceDays: '',
  });

  it('is null when nothing changed', () => {
    expect(projectBindsTrackerDiff(draftOf(false))).toBeNull();
  });

  it('carries bindsTracker when it was ticked', () => {
    expect(projectBindsTrackerDiff(draftOf(true))).toEqual({ bindsTracker: true });
  });
});

describe('projectStoriesOnEpicsDiff', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: true,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: [],
    reflectionCadenceDays: null,
  };
  const draftOf = (storiesOnEpics: boolean, was = false): ProjectDraft => ({
    known: { ...known, storiesOnEpics: was },
    name: known.name,
    key: known.key,
    color: known.color,
    icon: known.icon,
    repositories: known.repositories,
    landing: known.landing,
    checksRequired: known.checksRequired,
    onePullRequest: known.onePullRequest,
    autoHealBugEnabled: known.autoHealBugEnabled,
    autoHealBugPriority: known.autoHealBugPriority,
    autoHealBugPreApproval: known.autoHealBugPreApproval,
    autoHealBugStatusId: known.autoHealBugStatusId,
    repoSourced: known.repoSourced,
    bindsTracker: false,
    storiesOnEpics,
    checkApprovesMerge: false,
    runnerTags: '',
    reflectionCadenceDays: '',
  });

  it('is null when nothing changed', () => {
    expect(projectStoriesOnEpicsDiff(draftOf(false))).toBeNull();
  });

  it('carries storiesOnEpics when it was ticked', () => {
    expect(projectStoriesOnEpicsDiff(draftOf(true))).toEqual({ storiesOnEpics: true });
  });

  it('carries storiesOnEpics when a known-true project was unticked', () => {
    expect(projectStoriesOnEpicsDiff(draftOf(false, true))).toEqual({ storiesOnEpics: false });
  });
});

describe('projectDraftKeyObjection', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: false,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: [],
    reflectionCadenceDays: null,
  };
  const withKey = (key: string): ProjectDraft => ({ known, name: known.name, key, color: known.color, icon: known.icon, repositories: known.repositories, landing: known.landing, checksRequired: known.checksRequired, onePullRequest: known.onePullRequest, autoHealBugEnabled: known.autoHealBugEnabled, autoHealBugPriority: known.autoHealBugPriority, autoHealBugPreApproval: known.autoHealBugPreApproval, autoHealBugStatusId: known.autoHealBugStatusId, repoSourced: known.repoSourced, bindsTracker: false, storiesOnEpics: false, checkApprovesMerge: false, runnerTags: '', reflectionCadenceDays: '' });

  it('is no objection when the key is unchanged', () => {
    expect(projectDraftKeyObjection(withKey('AER'), '')).toBeNull();
  });

  it('is no objection when the key differs only by case or padding', () => {
    expect(projectDraftKeyObjection(withKey(' aer '), '')).toBeNull();
  });

  it('asks for the old key when the key changed and is unconfirmed', () => {
    expect(projectDraftKeyObjection(withKey('NEW'), '')).toBe('Type AER to confirm.');
  });

  it('is no objection when the key changed and the old key is typed', () => {
    expect(projectDraftKeyObjection(withKey('NEW'), 'aer')).toBeNull();
  });

  it('objects to a changed key that is invalid', () => {
    expect(projectDraftKeyObjection(withKey('1X'), 'AER')).not.toBeNull();
  });
});

describe('projectRunnerTagsDiff', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: false,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: ['macos', 'xcode'],
    reflectionCadenceDays: null,
  };
  const withTagsField = (runnerTags: string): ProjectDraft => ({
    known,
    name: known.name,
    key: known.key,
    color: known.color,
    icon: known.icon,
    repositories: known.repositories,
    landing: known.landing,
    checksRequired: known.checksRequired,
    onePullRequest: known.onePullRequest,
    autoHealBugEnabled: known.autoHealBugEnabled,
    autoHealBugPriority: known.autoHealBugPriority,
    autoHealBugPreApproval: known.autoHealBugPreApproval,
    autoHealBugStatusId: known.autoHealBugStatusId,
    repoSourced: known.repoSourced,
    bindsTracker: known.bindsTracker,
    storiesOnEpics: known.storiesOnEpics,
    checkApprovesMerge: known.checkApprovesMerge,
    runnerTags,
    reflectionCadenceDays: '',
  });

  it('is null when the textarea reparses to the same list as known.runnerTags', () => {
    expect(projectRunnerTagsDiff(withTagsField('macos\nxcode'))).toBeNull();
  });

  it('is null when the only difference is a blank line in the box', () => {
    expect(projectRunnerTagsDiff(withTagsField('macos\n\nxcode\n'))).toBeNull();
  });

  it('carries the new list when it changed', () => {
    expect(projectRunnerTagsDiff(withTagsField('macos\nwindows'))).toEqual({ tags: ['macos', 'windows'] });
  });

  it('carries a reordering of the same tags - order is significant', () => {
    expect(projectRunnerTagsDiff(withTagsField('xcode\nmacos'))).toEqual({ tags: ['xcode', 'macos'] });
  });
});

describe('projectReflectionDiff', () => {
  const known = {
    name: 'Aerial',
    key: 'AER',
    color: '#6b7280',
    icon: 'rocket',
    repositories: [{ remote: 'a', baseBranch: 'main' }],
    landing: 'pull-request' as const,
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: false,
    autoHealBugPriority: 'emergency' as const,
    autoHealBugPreApproval: 'express' as const,
    autoHealBugStatusId: null,
    repoSourced: false,
    bindsTracker: false,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    runnerTags: [],
    reflectionCadenceDays: null,
  };
  const withCadence = (reflectionCadenceDays: string, knownCadence: number | null): ProjectDraft => ({
    ...known,
    known: { ...known, reflectionCadenceDays: knownCadence },
    runnerTags: '',
    reflectionCadenceDays,
  });

  it('is null when the box still shows the known cadence', () => {
    expect(projectReflectionDiff(withCadence('7', 7))).toBeNull();
  });

  it('is null when the box is blank and the cadence was already off', () => {
    expect(projectReflectionDiff(withCadence('', null))).toBeNull();
  });

  it('carries the number when a cadence is set where there was none', () => {
    expect(projectReflectionDiff(withCadence('7', null))).toEqual({ cadenceDays: 7 });
  });

  it('carries the new number when the cadence changed', () => {
    expect(projectReflectionDiff(withCadence(' 14 ', 7))).toEqual({ cadenceDays: 14 });
  });

  it('carries null, not 0, when the box is cleared', () => {
    expect(projectReflectionDiff(withCadence('', 7))).toEqual({ cadenceDays: null });
    expect(projectReflectionDiff(withCadence('   ', 7))).toEqual({ cadenceDays: null });
  });

  it('sends text that is no number as 0, for the server to refuse, never as null', () => {
    expect(projectReflectionDiff(withCadence('weekly', null))).toEqual({ cadenceDays: 0 });
    expect(projectReflectionDiff(withCadence('weekly', 7))).toEqual({ cadenceDays: 0 });
  });
});
