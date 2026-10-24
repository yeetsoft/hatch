import { describe, expect, it } from 'vitest';
import {
  absentSentence,
  boundCanonicals,
  fileForProject,
  projectsReading,
  repoSourcedOverrideHint,
  repoSourcedReason,
  shortSha,
  sortedFiles,
  stateLabel,
  visibleFiles,
} from './playbookFiles';
import type { PlaybookFileDto, Project } from '../types';

const file = (over: Partial<PlaybookFileDto> = {}): PlaybookFileDto => ({
  id: 1,
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  sha: '1'.repeat(40),
  state: 'parsed',
  hash: 'deadbeef',
  rows: [],
  problem: null,
  lastGoodSha: null,
  lastGoodHash: null,
  url: null,
  checkedAt: '2026-09-09T12:00:00Z',
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  ...over,
});

const project = (over: Partial<Project> = {}): Project => ({
  id: 1,
  key: 'HA',
  name: 'Hatch',
  issueCount: 0,
  createdAt: '2026-09-09T12:00:00Z',
  color: null,
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  members: [],
  canApprove: false,
  canAdminister: false,
  landing: 'pull-request',
  checksRequired: false,
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
  ...over,
});

describe('stateLabel', () => {
  it('names parsed', () => {
    expect(stateLabel('parsed')).toBe('Parsed');
  });

  it('names invalid', () => {
    expect(stateLabel('invalid')).toBe('Invalid');
  });

  it('names absent', () => {
    expect(stateLabel('absent')).toBe('Absent');
  });
});

describe('absentSentence', () => {
  it('interpolates the trunk exactly', () => {
    expect(absentSentence('main')).toBe('no .hatch/playbooks.yaml on main');
  });
});

describe('shortSha', () => {
  it('slices a full sha to 7 characters', () => {
    expect(shortSha('a'.repeat(40))).toBe('aaaaaaa');
  });

  it('passes a shorter sha through unchanged', () => {
    expect(shortSha('abc')).toBe('abc');
  });
});

describe('boundCanonicals', () => {
  it('collects every repository across every project', () => {
    const projects = [
      project({ repositories: [{ remote: 'r1', canonical: 'forge.example/owner/one', baseBranch: null }] }),
      project({ repositories: [{ remote: 'r2', canonical: 'forge.example/owner/two', baseBranch: null }] }),
    ];

    expect(boundCanonicals(projects)).toEqual(
      new Set(['forge.example/owner/one', 'forge.example/owner/two']),
    );
  });

  it('answers an empty set for no projects', () => {
    expect(boundCanonicals([])).toEqual(new Set());
  });
});

describe('visibleFiles', () => {
  it('drops an absent file whose canonical is not bound', () => {
    const bound = new Set(['forge.example/owner/bound']);
    const files = [file({ canonical: 'forge.example/owner/unbound', state: 'absent' })];

    expect(visibleFiles(files, bound)).toEqual([]);
  });

  it('keeps an absent file whose canonical is bound', () => {
    const bound = new Set(['forge.example/owner/bound']);
    const files = [file({ canonical: 'forge.example/owner/bound', state: 'absent' })];

    expect(visibleFiles(files, bound)).toEqual(files);
  });

  it.each(['parsed', 'invalid'])('keeps a %s file regardless of binding', (state) => {
    const files = [file({ canonical: 'forge.example/owner/unbound', state })];

    expect(visibleFiles(files, new Set())).toEqual(files);
  });
});

describe('sortedFiles', () => {
  it('orders by canonical then by trunk on a tie', () => {
    const b = file({ canonical: 'forge.example/owner/b', trunk: 'main' });
    const aMain = file({ canonical: 'forge.example/owner/a', trunk: 'main' });
    const aDevelop = file({ canonical: 'forge.example/owner/a', trunk: 'develop' });

    expect(sortedFiles([b, aMain, aDevelop])).toEqual([aDevelop, aMain, b]);
  });
});

describe('projectsReading', () => {
  const f = file({ canonical: 'forge.example/owner/repo', trunk: 'main' });

  it('returns a repo-sourced project whose primary baseBranch names the file trunk', () => {
    const p = project({
      repoSourced: true,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: 'main' }],
    });

    expect(projectsReading(f, [p], [f])).toEqual([p]);
  });

  it('excludes a project whose primary baseBranch names a different trunk', () => {
    const p = project({
      repoSourced: true,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: 'develop' }],
    });

    expect(projectsReading(f, [p], [f])).toEqual([]);
  });

  it('returns a project with no baseBranch when it is the only loaded file for its canonical', () => {
    const p = project({
      repoSourced: true,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: null }],
    });

    expect(projectsReading(f, [p], [f])).toEqual([p]);
  });

  it('excludes a project with no baseBranch when two loaded files share its canonical', () => {
    const other = file({ canonical: f.canonical, trunk: 'develop' });
    const p = project({
      repoSourced: true,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: null }],
    });

    expect(projectsReading(f, [p], [f, other])).toEqual([]);
  });

  it('excludes a project that is not repo-sourced, regardless of its repositories', () => {
    const p = project({
      repoSourced: false,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: 'main' }],
    });

    expect(projectsReading(f, [p], [f])).toEqual([]);
  });

  it('excludes a repo-sourced project with no repositories', () => {
    const p = project({ repoSourced: true, repositories: [] });

    expect(projectsReading(f, [p], [f])).toEqual([]);
  });

  it('excludes a project whose primary canonical does not match the file at all', () => {
    const p = project({
      repoSourced: true,
      repositories: [{ remote: 'r', canonical: 'forge.example/owner/other', baseBranch: 'main' }],
    });

    expect(projectsReading(f, [p], [f])).toEqual([]);
  });
});

describe('fileForProject', () => {
  const f = file({ canonical: 'forge.example/owner/repo', trunk: 'main' });

  it("returns the file matching a repo-sourced project's canonical and base branch", () => {
    const p = project({
      repoSourced: true,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: 'main' }],
    });

    expect(fileForProject(p, [f])).toEqual(f);
  });

  it('falls back to the one file for the canonical when the project has no base branch', () => {
    const p = project({
      repoSourced: true,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: null }],
    });

    expect(fileForProject(p, [f])).toEqual(f);
  });

  it('returns null for a project that is not repo-sourced', () => {
    const p = project({
      repoSourced: false,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: 'main' }],
    });

    expect(fileForProject(p, [f])).toBeNull();
  });

  it.each(['absent', 'invalid'])('returns null when the project\'s file is %s', (state) => {
    const p = project({
      repoSourced: true,
      repositories: [{ remote: 'r', canonical: f.canonical, baseBranch: 'main' }],
    });
    const bad = file({ canonical: f.canonical, trunk: 'main', state });

    expect(fileForProject(p, [bad])).toBeNull();
  });
});

describe('repoSourcedReason', () => {
  it('names the key for a repo-sourced project', () => {
    const p = project({ key: 'AE', repoSourced: true });

    expect(repoSourcedReason(p)).toContain('AE');
  });

  it('returns null for a non-repo-sourced project', () => {
    const p = project({ repoSourced: false });

    expect(repoSourcedReason(p)).toBeNull();
  });
});

describe('repoSourcedOverrideHint', () => {
  it('returns the sentence for a repo-sourced project', () => {
    const p = project({ repoSourced: true });

    expect(repoSourcedOverrideHint(p)).toBe(
      "This project's playbooks come from .hatch/playbooks.yaml on its trunk; a board-wide row speaks where that file is silent.",
    );
  });

  it('returns null for a non-repo-sourced project', () => {
    const p = project({ repoSourced: false });

    expect(repoSourcedOverrideHint(p)).toBeNull();
  });
});
