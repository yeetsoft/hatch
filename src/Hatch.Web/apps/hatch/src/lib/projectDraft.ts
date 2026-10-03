/* The Projects edit dialog's draft, same known/working shape as lib/draft.ts
   but generalized to a record of fields - name, key, and the repositories
   list - rather than one string. Operates on ProjectRepositoryWriteRequest[],
   the PUT shape, like lib/repositories.ts does and for the same reason: a
   newly-added entry has no `canonical` yet. No DOM. */

import type { Project, ProjectPatchRequest, ProjectRepositoryWriteRequest } from '../types';

export interface ProjectDraft {
  known: { name: string; key: string; repositories: ProjectRepositoryWriteRequest[] };
  name: string;
  key: string;
  repositories: ProjectRepositoryWriteRequest[];
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
  return {
    known: { name: project.name, key: project.key, repositories },
    name: project.name,
    key: project.key,
    repositories,
  };
}

export function projectDraftDiff(draft: ProjectDraft): ProjectDraftDiff {
  const patchFields: ProjectPatchRequest = {};
  if (draft.name !== draft.known.name) patchFields.name = draft.name;
  if (draft.key !== draft.known.key) patchFields.key = draft.key;

  return {
    patch: Object.keys(patchFields).length > 0 ? patchFields : null,
    repositories: sameRepositories(draft.repositories, draft.known.repositories) ? null : draft.repositories,
  };
}
