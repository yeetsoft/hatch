/* What the finding's confirm-and-file dialog computes and sends, with no React
   in it so a node test can drive it, as trunkBuildBug.ts is driven. The fields,
   the defaults and the approval check are that file's, unchanged. */

import { message } from './errors';
import { bugRequest, type BugFields } from './trunkBuildBug';
import type { GooseFindingBugRequest, Issue } from '../types';

/** What a set of fields sends for a finding: the trunk build's request, plus the
    project. `projectKey` is the one the person chose, and is null for a finding
    that names its own - the route refuses a key that differs from the finding's. */
export function findingBugRequest(fields: BugFields, projectKey: string | null): GooseFindingBugRequest {
  return { ...bugRequest(fields), projectKey };
}

/** Files the bug with exactly what the fields show, or reports the server's
    own refusal. `post` is `fileGooseFindingBug` in the running app and a stub
    in the test. */
export async function fileFindingBug(
  findingId: number,
  fields: BugFields,
  projectKey: string | null,
  post: (id: number, request: GooseFindingBugRequest) => Promise<Issue>,
): Promise<{ ok: true; issue: Issue } | { ok: false; error: string }> {
  try {
    return { ok: true, issue: await post(findingId, findingBugRequest(fields, projectKey)) };
  } catch (err) {
    return { ok: false, error: message(err) };
  }
}
