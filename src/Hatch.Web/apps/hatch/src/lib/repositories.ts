/* The list arithmetic behind the Projects page's repositories cell.

   Operates on ProjectRepositoryWriteRequest[], the shape a PUT sends, rather
   than on ProjectRepository[]: a newly-added entry has no `canonical` yet, the
   server computes that on the way back. No DOM, same reasoning as
   lib/projectKey.ts and lib/dependencies.ts - the arithmetic is worth testing
   without a component in the way. */

import type { ProjectRepositoryWriteRequest } from '../types';

/** Appends. The first entry added to an empty list becomes primary for free. */
export function withRemote(
  list: ProjectRepositoryWriteRequest[],
  entry: ProjectRepositoryWriteRequest,
): ProjectRepositoryWriteRequest[] {
  return [...list, entry];
}

/** The list with that index removed. */
export function withoutRemote(list: ProjectRepositoryWriteRequest[], index: number): ProjectRepositoryWriteRequest[] {
  return list.filter((_, i) => i !== index);
}

/** Moves the entry at `from` to `to`. A `to` outside [0, list.length) is a
    no-op, which is what clamps the up/down buttons at either end of the list. */
export function moved(
  list: ProjectRepositoryWriteRequest[],
  from: number,
  to: number,
): ProjectRepositoryWriteRequest[] {
  if (to < 0 || to >= list.length) return list;

  const next = [...list];
  const [entry] = next.splice(from, 1);
  next.splice(to, 0, entry);
  return next;
}

/** null, or a sentence, when `remote.trim()` is empty - the one client-side
    check. Duplicate, oversized and unparseable are left to the server's
    refusal, per the outcome. */
export function repositoryObjection(remote: string): string | null {
  return remote.trim() === '' ? 'Give the repository a remote.' : null;
}
