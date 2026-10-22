/* The Projects edit dialog's draft, same known/working shape as lib/draft.ts
   but generalized to a record of fields - name, key, and the repositories
   list - rather than one string. Operates on ProjectRepositoryWriteRequest[],
   the PUT shape, like lib/repositories.ts does and for the same reason: a
   newly-added entry has no `canonical` yet. No DOM. */

import { normalizeProjectKey, rekeyObjection } from './projectKey';
import { runnerTagsDraft, runnerTagsRequest, sameTags } from './runnerTags';
import type { Priority } from '../components/PriorityControl';
import type { PreApproval } from '../components/PreApprovalControl';
import type {
  Project,
  ProjectLandingWriteRequest,
  ProjectPatchRequest,
  ProjectRepoSourcedWriteRequest,
  ProjectRepositoryWriteRequest,
  ProjectRunnerTagsWriteRequest,
} from '../types';

export interface ProjectDraft {
  known: {
    name: string;
    key: string;
    color: string | null;
    icon: string | null;
    repositories: ProjectRepositoryWriteRequest[];
    landing: 'pull-request' | 'trunk';
    checksRequired: boolean;
    onePullRequest: boolean;
    autoHealBugEnabled: boolean;
    autoHealBugPriority: Priority;
    autoHealBugPreApproval: PreApproval;
    autoHealBugStatusId: number | null;
    repoSourced: boolean;
    runnerTags: string[];
  };
  name: string;
  key: string;
  color: string | null;
  icon: string | null;
  repositories: ProjectRepositoryWriteRequest[];
  landing: 'pull-request' | 'trunk';
  checksRequired: boolean;
  onePullRequest: boolean;
  autoHealBugEnabled: boolean;
  autoHealBugPriority: Priority;
  autoHealBugPreApproval: PreApproval;
  autoHealBugStatusId: number | null;
  repoSourced: boolean;
  runnerTags: string;
}

export interface ProjectDraftDiff {
  patch: ProjectPatchRequest | null;
  repositories: ProjectRepositoryWriteRequest[] | null;
}

function sameRepositories(a: ProjectRepositoryWriteRequest[], b: ProjectRepositoryWriteRequest[]): boolean {
  if (a.length !== b.length) return false;
  return a.every((entry, i) => entry.remote === b[i].remote && entry.baseBranch === b[i].baseBranch);
}

export function openProjectDraft(project: Project): ProjectDraft {
  const repositories = project.repositories.map(({ remote, baseBranch }) => ({ remote, baseBranch }));
  const runnerTags = project.runnerTags ?? [];
  return {
    known: {
      name: project.name,
      key: project.key,
      color: project.color,
      icon: project.icon,
      repositories,
      landing: project.landing,
      checksRequired: project.checksRequired,
      onePullRequest: project.onePullRequest,
      autoHealBugEnabled: project.autoHealBugEnabled,
      autoHealBugPriority: project.autoHealBugPriority,
      autoHealBugPreApproval: project.autoHealBugPreApproval,
      autoHealBugStatusId: project.autoHealBugStatusId,
      repoSourced: project.repoSourced,
      runnerTags,
    },
    name: project.name,
    key: project.key,
    color: project.color,
    icon: project.icon,
    repositories,
    landing: project.landing,
    checksRequired: project.checksRequired,
    onePullRequest: project.onePullRequest,
    autoHealBugEnabled: project.autoHealBugEnabled,
    autoHealBugPriority: project.autoHealBugPriority,
    autoHealBugPreApproval: project.autoHealBugPreApproval,
    autoHealBugStatusId: project.autoHealBugStatusId,
    repoSourced: project.repoSourced,
    runnerTags: runnerTagsDraft(runnerTags),
  };
}

/** Why Save is greyed by the key, or null - a key left alone is no objection; a changed one meets the rekey speed bump. */
export function projectDraftKeyObjection(draft: ProjectDraft, confirmation: string): string | null {
  if (normalizeProjectKey(draft.key) === normalizeProjectKey(draft.known.key)) return null;
  return rekeyObjection(draft.known.key, draft.key, confirmation);
}

export function projectDraftDiff(draft: ProjectDraft): ProjectDraftDiff {
  const patchFields: ProjectPatchRequest = {};
  if (draft.name !== draft.known.name) patchFields.name = draft.name;
  if (draft.key !== draft.known.key) patchFields.key = draft.key;
  if (draft.color !== draft.known.color) patchFields.color = draft.color ?? '';
  if (draft.icon !== draft.known.icon) patchFields.icon = draft.icon ?? '';

  return {
    patch: Object.keys(patchFields).length > 0 ? patchFields : null,
    repositories: sameRepositories(draft.repositories, draft.known.repositories) ? null : draft.repositories,
  };
}

/** Why Save sends a landing PUT, or doesn't - a sparse diff the same way projectDraftDiff's patchFields is. */
export function projectLandingDiff(draft: ProjectDraft): ProjectLandingWriteRequest | null {
  const fields: ProjectLandingWriteRequest = {};
  if (draft.landing !== draft.known.landing) fields.landing = draft.landing;
  if (draft.checksRequired !== draft.known.checksRequired) fields.checksRequired = draft.checksRequired;
  if (draft.onePullRequest !== draft.known.onePullRequest) fields.onePullRequest = draft.onePullRequest;

  if (draft.autoHealBugEnabled !== draft.known.autoHealBugEnabled) fields.autoHealBugEnabled = draft.autoHealBugEnabled;
  if (draft.autoHealBugEnabled) {
    if (draft.autoHealBugPriority !== draft.known.autoHealBugPriority) fields.autoHealBugPriority = draft.autoHealBugPriority;
    if (draft.autoHealBugPreApproval !== draft.known.autoHealBugPreApproval)
      fields.autoHealBugPreApproval = draft.autoHealBugPreApproval;
    if (draft.autoHealBugStatusId !== draft.known.autoHealBugStatusId) {
      fields.autoHealBugStatusId = draft.autoHealBugStatusId === null ? '' : String(draft.autoHealBugStatusId);
    }
  }

  return Object.keys(fields).length > 0 ? fields : null;
}

/** Its own route and its own request shape - whether the project reads its
    playbooks from its repository, never folded into projectLandingDiff. */
export function projectRepoSourcedDiff(draft: ProjectDraft): ProjectRepoSourcedWriteRequest | null {
  return draft.repoSourced !== draft.known.repoSourced ? { repoSourced: draft.repoSourced } : null;
}

/** Its own route and its own request shape, like projectRepoSourcedDiff -
    compared against the committed list, not the raw textarea text, so a
    stray blank line in the box is not a change; order still matters,
    matching PutRunnerTags' own SequenceEqual on the server. */
export function projectRunnerTagsDiff(draft: ProjectDraft): ProjectRunnerTagsWriteRequest | null {
  const tags = runnerTagsRequest(draft.runnerTags);
  return sameTags(tags, draft.known.runnerTags) ? null : { tags };
}
